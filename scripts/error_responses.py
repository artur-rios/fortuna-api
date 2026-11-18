#!/usr/bin/env python3
"""Derive, from the source, the messages each operation's handler can fail with.

The OpenAPI document lists every response an operation can give, and most error responses come from the
handler: it fails with a message, and the action turns its output into a response with a status map
(`FortunaController.Respond`), which answers the status the map gives the first error, or 400 for one it does
not hold. Which messages a handler can fail with is what the code does, not metadata the API can read at run
time, so this script reads it from the source and writes it to
src/Presentation/ArturRios.Fortuna.WebApi/OpenApi/handler-messages.json. The Web API embeds that file, and
ErrorResponseOperationFilter looks each message up in the operation's status map, so the running API's
/swagger document and docs/openapi/fortuna.json say the same thing.

For each controller action:

- the handlers are, for each input it dispatches through a mediator (`SendAsync<TCommand, ...>`,
  `QueryAsync<...>`, `QueryPageAsync<...>`, `Commands.ExecuteCommandAsync<...>`, `Queries.ExecuteQueryAsync<...>`),
  the types that implement a handler interface for it (`ICommandHandlerAsync<TCommand, ...>`,
  `IQueryHandlerAsync<...>`, `IPaginatedQueryHandlerAsync<...>`);
- what it can fail with is every message constant (`<X>Messages.<Name>`) in the action, its handlers and the code
  they reach: the types it names, the types implementing the interfaces among them and the validator of the input
  (registered in front of the handler), transitively. Types are followed one by one, not file by file, since a
  file can hold several handlers; of another action's handler, only the members the code calls are followed
  (`LoginThroughApiCommandHandler.Failure`, and what it calls), not its own `HandleAsync`. Messages, inputs,
  outputs, domain types, controllers, registrations and migrations hold no code that fails a request and are not
  followed;
- the maps are the controller's status maps the action names (`RequestStatuses`), or the one its `StatusMap`
  returns, or the shared map;
- a handler whose code builds a `HeimdallRelayedRefusal` answers with the status of the outcome it relays.

It over-approximates on purpose: a message the reached code names is taken to be one the operation can answer
with. A status the document lists and the operation never answers costs a reader a line; one it answers and the
document leaves out misleads a client.

Usage (from anywhere in the repository):

    python scripts/error_responses.py           # rewrite the catalog
    python scripts/error_responses.py --check   # fail if the committed catalog is out of date

scripts/openapi.py runs it before generating the document, and its --check checks it too.
"""

import argparse
import json
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
CATALOG_PATH = Path("src/Presentation/ArturRios.Fortuna.WebApi/OpenApi/handler-messages.json")
CONTROLLERS = Path("src/Presentation/ArturRios.Fortuna.WebApi/Controllers")
SHARED_MAP = "Shared"
RELAYED_REFUSAL = "HeimdallRelayedRefusal"

# Folders whose types fail no request: following them would only add noise. Validators live under Input, and are
# reached through the input they validate.
EXCLUDED_FOLDERS = {
    "ArturRios.Fortuna.Domain", "Messages", "Controllers", "Extensions", "Input", "Output", "Migrations", "Seeding",
}
KEYWORDS = {"where", "new", "struct", "class", "record", "interface", "enum"}

TOKEN = re.compile(
    r'"""[\s\S]*?"""'  # raw string literal
    r'|@"(?:[^"]|"")*"'  # verbatim string literal
    r'|\$?@?"(?:[^"\\\n]|\\.)*"'  # string literal, interpolated or not
    r"|'(?:[^'\\\n]|\\.)'"  # character literal
    r"|//[^\n]*"  # line comment
    r"|/\*[\s\S]*?\*/"  # block comment
)
DECLARATION = re.compile(r"\b(class|record|struct|interface|enum)\s+(\w+)")
IDENTIFIER = re.compile(r"\b([A-Z]\w*)\b")
MESSAGE_REFERENCE = re.compile(r"\b(\w+Messages)\s*\.\s*([A-Z]\w*)\b")
HANDLER_INTERFACE = re.compile(r"^I\w*HandlerAsync$")
VALIDATOR_OF = re.compile(r"\bAbstractValidator\s*<\s*(\w+)\s*>")
DISPATCH = re.compile(
    r"\b(?:SendAsync|QueryAsync|QueryPageAsync|ExecuteCommandAsync|ExecuteQueryAsync|ExecutePaginatedQueryAsync)"
    r"\s*<\s*(\w+)")
MAP_FIELD = re.compile(r"IReadOnlyDictionary\s*<\s*string\s*,\s*int\s*>\s+(\w+)\s*=")
STATUS_MAP_PROPERTY = re.compile(r"override\s+IReadOnlyDictionary\s*<\s*string\s*,\s*int\s*>\s+StatusMap\s*=>\s*(\w+)")
ACTION_NAME = re.compile(r"(\w+)\s*\(")
# A method's declaration: its modifiers, return type and name, up to the parenthesis of its parameters.
MEMBER_DECLARATION = re.compile(
    r"^[ \t]*(?:(?:public|private|internal|protected|static|async|override|virtual|sealed|new)\s+)+"
    r"[\w<>\[\]?,. ]+?\b(\w+)\s*(?:<[^<>()]*>)?\s*\(", re.M)
FORTUNA_CONTROLLER = re.compile(r"\bclass\s+\w+Controller\b[^{]*:\s*FortunaController\b")


def code_only(text):
    """`text` without comments and with every string literal emptied: a type or message named in a comment or a
    string is not one the code uses."""

    def replace(match):
        token = match.group(0)
        if token.startswith("//") or token.startswith("/*"):
            return "\n" * token.count("\n")

        return '""'

    return TOKEN.sub(replace, text)


def matching(text, start, opening, closing):
    """The index just past the bracket that closes the one at `start`."""
    depth = 0
    for index in range(start, len(text)):
        if text[index] == opening:
            depth += 1
        elif text[index] == closing:
            depth -= 1
            if depth == 0:
                return index + 1

    return len(text)


def base_names(header):
    """The types a declaration's header names after its colon, without their generic arguments: the classes and
    interfaces it derives from, not its primary constructor's parameters or its constraints."""
    index = 0
    if index < len(header) and header[index] == "<":
        index = matching(header, index, "<", ">")
    while index < len(header) and header[index].isspace():
        index += 1
    if index < len(header) and header[index] == "(":
        index = matching(header, index, "(", ")")
    rest = header[index:].lstrip()
    if not rest.startswith(":"):
        return [], []

    bases = re.split(r"\bwhere\b", rest[1:], maxsplit=1)[0]
    # The generic arguments of each base, kept apart from its name.
    names, arguments = [], []
    for part in split_top_level(bases):
        name = re.match(r"\s*([\w.]+)", part)
        if name:
            names.append(name.group(1).split(".")[-1])
            generic = part.find("<")
            arguments.append([argument.strip() for argument in split_top_level(part[generic + 1:part.rfind(">")])]
                             if generic >= 0 else [])

    return names, arguments


def split_top_level(text):
    parts, depth, current = [], 0, []
    for character in text:
        if character in "<(":
            depth += 1
        elif character in ">)":
            depth -= 1
        if character == "," and depth == 0:
            parts.append("".join(current))
            current = []
        else:
            current.append(character)
    if "".join(current).strip():
        parts.append("".join(current))

    return parts


class Unit:
    """One type declaration: its name, kind, the code of its header and body, and what it derives from."""

    def __init__(self, name, kind, code, path, bases, base_arguments):
        self.name = name
        self.kind = kind
        self.code = code
        self.path = path
        self.bases = bases
        self.base_arguments = base_arguments

    def __repr__(self):
        return f"Unit({self.name})"


class Source:
    """The type declarations under src/, read once, with what the derivation needs indexed."""

    def __init__(self, root):
        self.root = root
        src = root / "src"
        files = sorted(path for path in src.rglob("*.cs") if not {"bin", "obj"} & set(path.relative_to(src).parts))
        self.units = []
        for path in files:
            self.units.extend(self._declarations(path, code_only(path.read_text(encoding="utf-8-sig"))))

        self.declared = {}
        self.implementers = {}
        self.validators = {}
        self.handlers = {}
        for unit in self.units:
            excluded = bool(EXCLUDED_FOLDERS & set(unit.path.relative_to(src).parts))
            if not excluded:
                self.declared.setdefault(unit.name, []).append(unit)
                for base in unit.bases:
                    self.implementers.setdefault(base, []).append(unit)
            for validated in VALIDATOR_OF.findall(unit.code.split("{", 1)[0]):
                self.validators.setdefault(validated, []).append(unit)
            for base, arguments in zip(unit.bases, unit.base_arguments):
                if HANDLER_INTERFACE.match(base) and arguments:
                    self.handlers.setdefault(arguments[0], []).append(unit)
        self.handler_units = {id(unit) for units in self.handlers.values() for unit in units}

    @staticmethod
    def _declarations(path, code):
        for match in DECLARATION.finditer(code):
            kind, name = match.groups()
            if name in KEYWORDS:
                continue
            # The body starts at the first brace (or a bodiless record's semicolon) past the name's generic
            # arguments and any parameter list.
            index = match.end()
            while index < len(code) and code[index] not in "{;":
                if code[index] == "(":
                    index = matching(code, index, "(", ")")
                    continue
                if code[index] == "<":
                    index = matching(code, index, "<", ">")
                    continue
                index += 1
            body = index
            end = matching(code, body, "{", "}") if body < len(code) and code[body] == "{" else body + 1
            header = code[match.end():body]
            bases, arguments = base_names(header)
            yield Unit(name, kind, code[match.start():end], path, bases, arguments)

    def reached(self, start):
        """The code `start` reaches, `start` included: whole types, except another action's handler, of which
        only the members this code calls (`LoginThroughApiCommandHandler.Failure`) are followed. A handler's own
        messages are what it fails with; a helper it lends another handler fails with its own only."""
        seen = {}
        pending = list(start)
        starts = {id(unit) for unit in start}
        while pending:
            unit = pending.pop()
            key = (unit.path, unit.name) if unit.kind == "member" else id(unit)
            if key in seen:
                continue
            seen[key] = unit
            for name in set(IDENTIFIER.findall(unit.code)):
                for declared in self.declared.get(name, ()):
                    if id(declared) in self.handler_units and id(declared) not in starts:
                        pending.extend(self.members(declared, unit.code))
                        continue
                    pending.append(declared)
                    if declared.kind == "interface":
                        pending.extend(self.implementers.get(name, ()))

        return list(seen.values())

    @staticmethod
    def members(owner, caller):
        """The members of `owner` that `caller` names as `Owner.Member`, each as a unit of its own code, and the
        members of `owner` those call in turn."""
        bodies = {}
        for declaration in MEMBER_DECLARATION.finditer(owner.code):
            index = matching(owner.code, declaration.end() - 1, "(", ")")
            while index < len(owner.code) and owner.code[index] != "{" and owner.code[index:index + 2] != "=>":
                index += 1
            if owner.code[index:index + 1] == "{":
                end = matching(owner.code, index, "{", "}")
            else:
                end, depth = index, 0
                while end < len(owner.code) and not (owner.code[end] == ";" and depth == 0):
                    depth += 1 if owner.code[end] in "({[" else -1 if owner.code[end] in ")}]" else 0
                    end += 1
            bodies.setdefault(declaration.group(1), []).append(owner.code[declaration.start():end])

        wanted = sorted(set(re.findall(rf"\b{owner.name}\s*\.\s*(\w+)", caller)) & set(bodies))
        found = set()
        while wanted:
            member = wanted.pop()
            if member in found:
                continue
            found.add(member)
            for body in bodies[member]:
                wanted.extend(name for name in re.findall(r"\b(\w+)\s*(?:<[^<>()]*>)?\s*\(", body)
                              if name in bodies and name not in found)

        for member in sorted(found):
            yield Unit(f"{owner.name}.{member}", "member", "\n".join(bodies[member]), owner.path, [], [])


def actions(root):
    """Every action of a FortunaController: (operation, its code, the controller's code)."""
    for path in sorted((root / CONTROLLERS).glob("*Controller.cs")):
        code = code_only(path.read_text(encoding="utf-8-sig")).replace("\r\n", "\n")
        if not FORTUNA_CONTROLLER.search(code):
            continue  # a ControllerBase (the health checks): no handler, no status map
        controller = path.stem[:-len("Controller")]
        lines = code.split("\n")
        start = 0
        index = 0
        while index < len(lines):
            line = lines[index]
            if line.startswith("    public ") and "(" in "\n".join(lines[index:index + 3]):
                # A block body opens with a brace of its own at the member's indent and ends with its closing
                # one; an expression body ends with the first line that ends the statement.
                end = index
                while end < len(lines) and lines[end].rstrip() != "    {" and not lines[end].rstrip().endswith(";"):
                    end += 1
                if end < len(lines) and lines[end].rstrip() == "    {":
                    while end < len(lines) and lines[end].rstrip() != "    }":
                        end += 1
                attributes = "\n".join(lines[start:index])
                member = "\n".join(lines[index:end + 1])
                if "[Http" in attributes:
                    yield f"{controller}.{ACTION_NAME.search(member).group(1)}", member, code
                index = end
                start = end + 1
            elif line.rstrip() in ("    }", "    };") or line.strip() == "":
                start = index + 1
            index += 1


def derive(root=REPO_ROOT):
    """{operation: {handlers, maps, messages, relayed}} for every controller action."""
    source = Source(root)
    catalog = {}
    for operation, member, controller in actions(root):
        fields = set(MAP_FIELD.findall(controller))
        named = sorted({name for name in IDENTIFIER.findall(member) if name in fields})
        default = STATUS_MAP_PROPERTY.search(controller)
        maps = named or [default.group(1) if default else SHARED_MAP]

        handlers = []
        for input_name in dict.fromkeys(DISPATCH.findall(member)):
            found = source.handlers.get(input_name, [])
            if not found:
                raise SystemExit(f"{operation}: no handler implements a handler interface for {input_name}")
            handlers.extend(found)
            handlers.extend(source.validators.get(input_name, ()))

        # The action's own code too: it can fail with a message of its own (LocalAccounts.PasswordReset).
        action = Unit(operation, "action", member, root / CONTROLLERS, [], [])
        reached = source.reached(handlers + [action])
        messages = sorted({f"{owner}.{name}" for unit in reached
                           for owner, name in MESSAGE_REFERENCE.findall(unit.code)})
        catalog[operation] = {
            "handlers": sorted({unit.name for unit in handlers}),
            "maps": maps,
            "messages": messages,
            "relayed": any(RELAYED_REFUSAL in unit.code for unit in reached),
        }

    return dict(sorted(catalog.items()))


def render(catalog):
    return json.dumps(catalog, indent=2, sort_keys=True) + "\n"


def write(root=REPO_ROOT):
    (root / CATALOG_PATH).write_bytes(render(derive(root)).encode("utf-8"))
    print(f"Wrote {CATALOG_PATH.as_posix()}")


def check(root=REPO_ROOT):
    """0 when the committed catalog is what the source derives, 1 otherwise."""
    path = root / CATALOG_PATH
    current = path.read_text(encoding="utf-8").replace("\r\n", "\n") if path.exists() else ""
    if current == render(derive(root)):
        print(f"{CATALOG_PATH.as_posix()} is current.")

        return 0

    print(f"{CATALOG_PATH.as_posix()} is not current. Run scripts/openapi.py.", file=sys.stderr)

    return 1


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true", help="fail if the committed catalog is out of date")
    if parser.parse_args().check:
        return check()

    write()

    return 0


if __name__ == "__main__":
    sys.exit(main())
