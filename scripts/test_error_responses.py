"""Unit tests for error_responses.py, the derivation of the messages each action's handlers can fail with.

Run from the repository root:  python -m unittest discover -s scripts -p "test_*.py"

Each case builds a small source tree in a temporary directory with the repository's layout -- a controller, its
handlers, the code they reach and their messages -- and reads what the derivation makes of it. The last case holds
the committed catalog to the repository's own source.
"""

import tempfile
import textwrap
import unittest
from pathlib import Path

import error_responses

CONTROLLER = """\
[ApiController]
[Route("api/widgets")]
public sealed class WidgetsController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [WidgetMessages.NotFound] = StatusCodes.Status404NotFound
        });

    private static readonly IReadOnlyDictionary<string, int> RenameStatuses =
        FortunaStatusMap.With(new Dictionary<string, int>());

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpPost]
    public async Task<ActionResult<DataOutput<WidgetOutput?>>> Create([FromBody] CreateWidgetCommand command)
    {
        return await SendAsync<CreateWidgetCommand, WidgetOutput>(command);
    }

    [HttpPut("{id:guid}/name")]
    public async Task<ActionResult<DataOutput<WidgetOutput?>>> Rename(
        Guid id,
        [FromBody] RenameWidgetCommand command)
    {
        return await SendAsync<
            RenameWidgetCommand,
            WidgetOutput>(command, RenameStatuses);
    }

    [HttpPost("reset")]
    public ActionResult<DataOutput<object?>> Reset()
    {
        return Respond(DataOutput<object?>.New.WithError(WidgetMessages.ResetUnavailable));
    }

    public static string NotAnAction() => WidgetMessages.Ignored;
}
"""

HANDLERS = """\
namespace Sample.Command.Handlers;

public sealed class CreateWidgetCommandHandler(IWidgetStore store)
    : ICommandHandlerAsync<CreateWidgetCommand, WidgetOutput>
{
    // WidgetMessages.InComment is named in a comment only.
    public async Task<DataOutput<WidgetOutput?>> HandleAsync(CreateWidgetCommand command, CancellationToken token)
    {
        var text = "WidgetMessages.InString";

        return await store.CreateAsync(command)
            ? Ok()
            : LegacyWidgetCommandHandler.Refuse(WidgetMessages.Taken);
    }
}

public sealed class RenameWidgetCommandHandler : ICommandHandlerAsync<RenameWidgetCommand, WidgetOutput>
{
    public Task<DataOutput<WidgetOutput?>> HandleAsync(RenameWidgetCommand command, CancellationToken token) =>
        new HeimdallRelayedRefusal<WidgetOutput?>().WithError(WidgetMessages.RenameRefused);
}
"""

LEGACY = """\
namespace Sample.Command.Handlers;

public sealed class LegacyWidgetCommandHandler : ICommandHandlerAsync<LegacyWidgetCommand, WidgetOutput>
{
    public Task<DataOutput<WidgetOutput?>> HandleAsync(LegacyWidgetCommand command, CancellationToken token) =>
        Task.FromResult(Refuse(WidgetMessages.LegacyOnly));

    internal static DataOutput<WidgetOutput?> Refuse(string message) =>
        message == WidgetMessages.Taken ? Wrap(message) : Wrap(WidgetMessages.LegacyShared);

    private static DataOutput<WidgetOutput?> Wrap(string message)
    {
        return DataOutput<WidgetOutput?>.New.WithError(message ?? WidgetMessages.LegacyWrapped);
    }
}
"""

STORE_INTERFACE = """\
namespace Sample.Shared;

public interface IWidgetStore
{
    Task<bool> CreateAsync(CreateWidgetCommand command);
}
"""

STORE = """\
namespace Sample.Data;

public sealed class EfWidgetStore(AppDbContext context, WidgetClock clock) : IWidgetStore, IDisposable
{
    public Task<bool> CreateAsync(CreateWidgetCommand command) => throw new(WidgetMessages.StoreFailed);
}
"""

DOMAIN = """\
namespace Sample.Domain;

public sealed class WidgetClock
{
    public string Now() => WidgetMessages.FromDomain;
}
"""

VALIDATOR = """\
namespace Sample.Command.Input.Validation;

public sealed class CreateWidgetCommandValidator : AbstractValidator<CreateWidgetCommand>
{
    public CreateWidgetCommandValidator() => RuleFor(command => command.Name).NotEmpty()
        .WithMessage(WidgetMessages.NameRequired);
}
"""

HEALTH = """\
[ApiController]
public sealed class HealthCheckController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(WidgetMessages.Healthy);
}
"""

FILES = {
    "src/Presentation/ArturRios.Fortuna.WebApi/Controllers/WidgetsController.cs": CONTROLLER,
    "src/Presentation/ArturRios.Fortuna.WebApi/Controllers/HealthCheckController.cs": HEALTH,
    "src/Application/Sample.Command/Handlers/WidgetCommandHandlers.cs": HANDLERS,
    "src/Application/Sample.Command/Handlers/LegacyWidgetCommandHandler.cs": LEGACY,
    "src/Application/Sample.Command/Input/Validation/CreateWidgetCommandValidator.cs": VALIDATOR,
    "src/Application/Sample.Shared/IWidgetStore.cs": STORE_INTERFACE,
    "src/Infrastructure/Sample.Data/EfWidgetStore.cs": STORE,
    "src/Domain/ArturRios.Fortuna.Domain/WidgetClock.cs": DOMAIN,
}


def derive(files=None):
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        for relative, text in (files or FILES).items():
            path = root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(textwrap.dedent(text), encoding="utf-8")

        return error_responses.derive(root)


class DeriveTests(unittest.TestCase):
    def test_given_a_dispatching_action_when_derived_then_its_handler_validator_and_reached_code_name_its_messages(self):
        entry = derive()["Widgets.Create"]

        self.assertEqual(["CreateWidgetCommandHandler", "CreateWidgetCommandValidator"], entry["handlers"])
        self.assertEqual(
            [
                "WidgetMessages.LegacyShared",
                "WidgetMessages.LegacyWrapped",
                "WidgetMessages.NameRequired",
                "WidgetMessages.StoreFailed",
                "WidgetMessages.Taken",
            ],
            entry["messages"],
        )

    def test_given_two_handlers_in_one_file_when_derived_then_each_action_gets_only_its_own(self):
        catalog = derive()

        self.assertNotIn("WidgetMessages.RenameRefused", catalog["Widgets.Create"]["messages"])
        self.assertEqual(["WidgetMessages.RenameRefused"], catalog["Widgets.Rename"]["messages"])

    def test_given_another_handlers_helper_when_derived_then_only_the_members_called_are_followed(self):
        messages = derive()["Widgets.Create"]["messages"]

        # Refuse, and Wrap which it calls, are followed; the other handler's HandleAsync is not.
        self.assertIn("WidgetMessages.LegacyShared", messages)
        self.assertIn("WidgetMessages.LegacyWrapped", messages)
        self.assertNotIn("WidgetMessages.LegacyOnly", messages)

    def test_given_comments_strings_and_domain_types_when_derived_then_they_name_no_message(self):
        messages = derive()["Widgets.Create"]["messages"]

        for absent in ("WidgetMessages.InComment", "WidgetMessages.InString", "WidgetMessages.FromDomain"):
            self.assertNotIn(absent, messages)

    def test_given_an_action_naming_a_map_when_derived_then_that_map_replaces_the_default(self):
        catalog = derive()

        self.assertEqual(["Statuses"], catalog["Widgets.Create"]["maps"])
        self.assertEqual(["RenameStatuses"], catalog["Widgets.Rename"]["maps"])

    def test_given_a_controller_without_a_map_when_derived_then_the_shared_map_is_used(self):
        files = dict(FILES)
        path = "src/Presentation/ArturRios.Fortuna.WebApi/Controllers/WidgetsController.cs"
        files[path] = CONTROLLER.replace("    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;\n", "")

        self.assertEqual(["Shared"], derive(files)["Widgets.Create"]["maps"])

    def test_given_an_action_without_a_mediator_when_derived_then_its_own_messages_are_listed(self):
        entry = derive()["Widgets.Reset"]

        self.assertEqual([], entry["handlers"])
        self.assertEqual(["WidgetMessages.ResetUnavailable"], entry["messages"])

    def test_given_a_relayed_refusal_when_derived_then_only_its_action_is_relayed(self):
        catalog = derive()

        self.assertTrue(catalog["Widgets.Rename"]["relayed"])
        self.assertFalse(catalog["Widgets.Create"]["relayed"])

    def test_given_members_that_are_not_actions_or_controllers_without_maps_when_derived_then_they_are_left_out(self):
        catalog = derive()

        self.assertEqual(["Widgets.Create", "Widgets.Rename", "Widgets.Reset"], list(catalog))

    def test_given_an_input_no_handler_handles_when_derived_then_it_fails(self):
        files = dict(FILES)
        files["src/Application/Sample.Command/Handlers/WidgetCommandHandlers.cs"] = HANDLERS.replace(
            "ICommandHandlerAsync<CreateWidgetCommand, WidgetOutput>", "IDisposable")

        with self.assertRaises(SystemExit):
            derive(files)


class CommittedCatalogTests(unittest.TestCase):
    def test_given_the_repository_when_derived_then_the_committed_catalog_is_current(self):
        committed = (error_responses.REPO_ROOT / error_responses.CATALOG_PATH).read_text(encoding="utf-8")

        self.assertEqual(committed.replace("\r\n", "\n"), error_responses.render(error_responses.derive()))


if __name__ == "__main__":
    unittest.main()
