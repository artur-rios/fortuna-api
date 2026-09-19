#!/usr/bin/env python3
"""Fail when a return, break or yield statement in C# directly follows another statement.

The repository style (also captured in .editorconfig as
`resharper_blank_lines_before_control_transfer_statements = 1`) wants a full blank line above
every `return`, `break`, `yield return` and `yield break` that follows another statement. The IDE
formatter applies it but nothing enforced it, so this script is what CI runs.

A statement does not need the blank line when it is:

  * the first statement in its block (the line above ends with `{`);
  * the first statement in a switch section (the line above is a `case`/`default` label);
  * the body of a brace-less `if`/`else`/`for`/`foreach`/`while`, including one whose condition
    spans several lines (the line above ends with `)` rather than `);`).

Comments attached to the statement stay attached: the blank line belongs above the comment block,
not between the comment and the statement. Migrations folders (generated code) and the contents of
raw string literals are skipped.

Usage (from anywhere in the repository):

    python scripts/check_blank_lines.py            # check src/ and tests/
    python scripts/check_blank_lines.py FILE...    # check only the given files
"""

import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
SEARCH_ROOTS = ("src", "tests")
SKIPPED_DIRECTORIES = {"bin", "obj", "Migrations"}
RAW_STRING_DELIMITER = '"""'

STATEMENT = re.compile(r"^\s*(return\b|break\s*;|yield\s+(return|break)\b)")
LABEL = re.compile(r"^(case\b.*|default)\s*:$")
BRACELESS_HEADER = re.compile(r"^(if|else\s+if|while|for|foreach)\s*\(.*\)$|^else$")


def is_exempt(above):
    """Whether the (stripped) line above the statement or its comment block exempts it."""
    return (
        above == ""
        or above.endswith("{")
        or bool(LABEL.match(above))
        or bool(BRACELESS_HEADER.match(above))
        # The last line of a condition that spans several lines: the statement is its body.
        or (above.endswith(")") and not above.endswith(");"))
    )


def find_violations(text):
    """Return the 1-based line numbers of statements missing the blank line above them."""
    lines = text.splitlines()
    violations = []
    in_raw_string = False
    for index, line in enumerate(lines):
        # Decide on the state at the start of the line: `return $"""` opens a raw string and is
        # itself a return statement, while a line inside the literal is text, not code.
        starts_inside_raw_string = in_raw_string
        if line.count(RAW_STRING_DELIMITER) % 2 == 1:
            in_raw_string = not in_raw_string
        if starts_inside_raw_string or not STATEMENT.match(line):
            continue

        anchor = index
        while anchor > 0 and lines[anchor - 1].strip().startswith("//"):
            anchor -= 1
        above = lines[anchor - 1].strip() if anchor > 0 else ""
        if not is_exempt(above):
            violations.append(index + 1)

    return violations


def is_checked(path):
    return path.suffix == ".cs" and not SKIPPED_DIRECTORIES.intersection(path.parts)


def discover_files(root=REPO_ROOT):
    files = []
    for search_root in SEARCH_ROOTS:
        base = root / search_root
        if base.is_dir():
            files.extend(
                path for path in base.rglob("*.cs")
                if is_checked(path.relative_to(root))
            )

    return sorted(files)


def check(paths):
    """Print one line per violation and return how many were found."""
    count = 0
    for path in paths:
        text = Path(path).read_text(encoding="utf-8-sig")
        for line_number in find_violations(text):
            print(f"{path}:{line_number}: add a blank line above this statement")
            count += 1

    return count


def main(argv):
    paths = [Path(argument) for argument in argv] if argv else discover_files()
    count = check(paths)
    if count:
        print(
            f"{count} return/break/yield statement(s) need a blank line above them.",
            file=sys.stderr,
        )

        return 1

    print(f"Checked {len(paths)} files: every return/break/yield statement is spaced.")

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
