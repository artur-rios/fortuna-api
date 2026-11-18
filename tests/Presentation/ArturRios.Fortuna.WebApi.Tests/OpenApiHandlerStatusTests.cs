using System.Reflection;
using System.Runtime.CompilerServices;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.WebApi.Controllers;
using ArturRios.Fortuna.WebApi.OpenApi;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
///     Ties the handler error statuses docs/openapi/fortuna.json lists to what each action's handler, and the code
///     it reaches, can fail with: the messages scripts/error_responses.py reads from the source
///     (OpenApi/handler-messages.json), looked up in the action's status map. Both ways: every status a reached
///     message maps to is listed as the handler's, and every status listed as the handler's is backed by such a
///     message (or is 400, or a relayed outcome's). The handlers' compiled code then checks the catalog itself:
///     every mapped message a handler loads, and every handler of an input an action dispatches, is in it.
/// </summary>
public sealed class OpenApiHandlerStatusTests
{
    private const string HandlerSentence = "The handler refused the request";

    private const BindingFlags Statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly string[] Dispatchers =
    [
        "SendAsync", "QueryAsync", "QueryPageAsync", "ExecuteCommandAsync", "ExecuteQueryAsync",
        "ExecutePaginatedQueryAsync"
    ];

    private static readonly Assembly[] HandlerAssemblies =
    [
        typeof(CreateFinancialAccountCommand).Assembly,
        typeof(ConvertFigureQuery).Assembly,
        typeof(UserProfileMessages).Assembly
    ];

    [UnitFact]
    public void GivenEveryFortunaAction_WhenDocumented_ThenTheHandlerAnswersExactlyTheStatusesItsReachedMessagesMap()
    {
        var checkedStatuses = 0;
        foreach (var (key, action) in FortunaActions())
        {
            var expected = Expected(action);
            using var contract = CommittedOpenApiDocument.Load();
            var documented = Responses(contract, key)
                .Where(response => response.Status >= 400 && response.Description.Contains(HandlerSentence,
                    StringComparison.Ordinal))
                .Select(response => response.Status)
                .ToHashSet();

            // A reached message's status that the document leaves to other sources, or does not list.
            foreach (var (status, backing) in expected.Statuses)
            {
                Assert.True(documented.Contains(status),
                    $"{key}'s handler can answer {status} ({string.Join(", ", backing)}), which the document " +
                    "does not list as the handler's.");
            }

            // A status the document gives the handler that no reached message, no unmapped failure and no
            // relayed outcome answers.
            foreach (var status in documented)
            {
                Assert.True(expected.Statuses.ContainsKey(status),
                    $"{key} lists {status} as the handler's, but nothing its handler reaches answers it.");
            }

            checkedStatuses += documented.Count;
        }

        Assert.True(checkedStatuses > 300, $"Only {checkedStatuses} handler statuses were checked.");
    }

    [UnitFact]
    public void GivenEachHandler_WhenItsCompiledCodeIsRead_ThenEveryMappedMessageItLoadsIsInTheCatalog()
    {
        var checkedHandlers = 0;
        foreach (var (key, action) in FortunaActions())
        {
            var entry = HandlerMessageCatalog.Entries[key];
            var catalogued = entry.Messages.Select(HandlerMessageCatalog.Text).OfType<string>().ToHashSet();
            var maps = entry.Maps.Select(name => HandlerMessageCatalog.Map(action.DeclaringType!, name)).ToArray();
            var code = entry.Handlers.Select(HandlerType).SelectMany(IlReader.Bodies).Concat(ActionBodies(action));

            foreach (var text in code.SelectMany(IlReader.Strings).Distinct())
            {
                var mapped = maps.Any(map => map.TryGetValue(text, out var status) && status >= 400);

                Assert.False(mapped && !catalogued.Contains(text),
                    $"{key}'s handlers load \"{text}\", which its status map answers with an error, but the " +
                    "catalog does not list it.");
            }

            checkedHandlers += entry.Handlers.Count;
        }

        Assert.True(checkedHandlers > 200, $"Only {checkedHandlers} handlers were read.");
    }

    [UnitFact]
    public void GivenEachAction_WhenItsCompiledCodeIsRead_ThenTheCatalogNamesTheHandlerOfEveryInputItDispatches()
    {
        var dispatching = 0;
        foreach (var (key, action) in FortunaActions())
        {
            var inputs = ActionBodies(action)
                .SelectMany(IlReader.Calls)
                .OfType<MethodInfo>()
                .Where(call => call.IsGenericMethod && Dispatchers.Contains(call.Name))
                .Select(call => call.GetGenericArguments()[0])
                .Distinct()
                .ToArray();
            var handlers = HandlerMessageCatalog.Entries[key].Handlers;

            foreach (var input in inputs)
            {
                var implementing = HandlerAssemblies.SelectMany(assembly => assembly.GetTypes())
                    .Where(type => type.GetInterfaces().Any(contract => contract.IsGenericType &&
                                                                        contract.Name.EndsWith("HandlerAsync`2",
                                                                            StringComparison.Ordinal) &&
                                                                        contract.GetGenericArguments()[0] == input))
                    .Select(type => type.Name)
                    .ToArray();

                Assert.NotEmpty(implementing);
                Assert.All(implementing, name => Assert.Contains(name, handlers));
            }

            dispatching += inputs.Length > 0 ? 1 : 0;
        }

        Assert.True(dispatching > 130, $"Only {dispatching} actions dispatch through a mediator.");
    }

    [UnitFact]
    public void GivenTheCatalog_WhenRead_ThenItListsEveryFortunaActionAndEachMessageAndMapItNamesExists()
    {
        var actions = FortunaActions().ToDictionary(action => action.Key, action => action.Value);

        Assert.Equal(actions.Keys.Order(StringComparer.Ordinal),
            HandlerMessageCatalog.Entries.Keys.Order(StringComparer.Ordinal));
        foreach (var (key, entry) in HandlerMessageCatalog.Entries)
        {
            Assert.All(entry.Maps, name => HandlerMessageCatalog.Map(actions[key].DeclaringType!, name));
            Assert.All(entry.Handlers, name => HandlerType(name));
            Assert.All(entry.Messages, message => Assert.True(Resolves(message), $"{key} names {message}."));
        }
    }

    /// <summary>
    ///     What the action's handlers can answer, worked out here from the catalog and the action's real status
    ///     maps: each status with what answers it, and whether a relayed refusal's 429 carries Retry-After.
    /// </summary>
    internal static (IReadOnlyDictionary<int, IReadOnlyList<string>> Statuses, bool RelaysRetryAfter) Expected(
        MethodInfo action)
    {
        var controller = action.DeclaringType!;
        var entry = HandlerMessageCatalog.Entries[$"{controller.Name[..^"Controller".Length]}.{action.Name}"];
        var statuses = new Dictionary<int, List<string>> { [400] = ["a failure no map holds"] };

        void Add(int status, string why)
        {
            if (!statuses.TryGetValue(status, out var backing))
            {
                statuses[status] = backing = [];
            }

            backing.Add(why);
        }

        foreach (var name in entry.Maps)
        {
            var map = name == HandlerMessages.SharedMap
                ? FortunaStatusMap.Shared
                : (IReadOnlyDictionary<string, int>)controller.GetField(name, Statics)!.GetValue(null)!;
            foreach (var message in entry.Messages)
            {
                if (Constant(message) is { } text && map.TryGetValue(text, out var status) && status >= 400)
                {
                    Add(status, message);
                }
            }
        }

        var relaysRetryAfter = false;
        if (entry.Relayed)
        {
            foreach (var field in controller.GetFields(Statics).Where(field =>
                         field.GetValue(null) is System.Collections.IDictionary &&
                         field.FieldType.GetGenericArguments() is [var keyType, var valueType] &&
                         keyType != typeof(string) && valueType == typeof(int)))
            {
                foreach (var status in ((System.Collections.IDictionary)field.GetValue(null)!).Values.Cast<int>())
                {
                    Add(status, $"a relayed refusal ({field.Name})");
                    relaysRetryAfter |= status == 429;
                }
            }
        }

        return (statuses.ToDictionary(pair => pair.Key, IReadOnlyList<string> (pair) => pair.Value), relaysRetryAfter);
    }

    private static IEnumerable<KeyValuePair<string, MethodInfo>> FortunaActions() =>
        OpenApiDocumentAccuracyTests.Actions()
            .Where(action => action.Value.DeclaringType!.IsSubclassOf(typeof(FortunaController)))
            .Select(action => KeyValuePair.Create(
                $"{action.Value.DeclaringType!.Name[..^"Controller".Length]}.{action.Value.Name}", action.Value))
            .DistinctBy(action => action.Key);

    private static IEnumerable<(int Status, string Description)> Responses(
        System.Text.Json.JsonDocument contract,
        string key)
    {
        foreach (var (operation, action) in OpenApiDocumentAccuracyTests.Actions())
        {
            if ($"{action.DeclaringType!.Name[..^"Controller".Length]}.{action.Name}" != key)
            {
                continue;
            }

            var separator = operation.IndexOf(' ', StringComparison.Ordinal);
            foreach (var response in contract.RootElement.GetProperty("paths")
                         .GetProperty(operation[(separator + 1)..])
                         .GetProperty(operation[..separator].ToLowerInvariant())
                         .GetProperty("responses").EnumerateObject())
            {
                yield return (int.Parse(response.Name, System.Globalization.CultureInfo.InvariantCulture),
                    response.Value.GetProperty("description").GetString()!);
            }
        }
    }

    /// <summary>The action's own code: its async state machine when it has one.</summary>
    private static IEnumerable<MethodBase> ActionBodies(MethodInfo action) =>
        action.GetCustomAttribute<AsyncStateMachineAttribute>() is { } machine
            ? IlReader.Bodies(machine.StateMachineType)
            : [action];

    private static Type HandlerType(string name) =>
        Assert.Single(HandlerAssemblies.SelectMany(assembly => assembly.GetTypes()),
            type => type.Name == name && !type.IsNested);

    private static string? Constant(string message)
    {
        var separator = message.IndexOf('.', StringComparison.Ordinal);
        var owner = typeof(UserProfileMessages).Assembly.GetTypes()
            .SingleOrDefault(type => type.Name == message[..separator]);

        return owner?.GetField(message[(separator + 1)..], BindingFlags.Public | BindingFlags.Static) is
        { IsLiteral: true } field
            ? (string?)field.GetRawConstantValue()
            : null;
    }

    /// <summary>A message constant, or a method of a messages class that builds one.</summary>
    private static bool Resolves(string message)
    {
        var separator = message.IndexOf('.', StringComparison.Ordinal);
        var owner = typeof(UserProfileMessages).Assembly.GetTypes()
            .SingleOrDefault(type => type.Name == message[..separator]);
        var member = message[(separator + 1)..];

        return owner is not null &&
               (owner.GetField(member, BindingFlags.Public | BindingFlags.Static) is not null ||
                owner.GetMethods(BindingFlags.Public | BindingFlags.Static).Any(method => method.Name == member));
    }
}
