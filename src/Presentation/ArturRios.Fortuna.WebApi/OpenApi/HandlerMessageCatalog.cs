using System.Reflection;
using System.Text.Json;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.WebApi.Controllers;

namespace ArturRios.Fortuna.WebApi.OpenApi;

/// <summary>
///     What an action's handlers can fail with, as scripts/error_responses.py derives it from the source:
///     the handler types, the status maps the action resolves their output with, the message constants the
///     action, its handlers and the code they reach name, and whether that code relays a Heimdall refusal.
/// </summary>
/// <param name="Handlers">The types that handle the inputs the action dispatches, and the inputs' validators.</param>
/// <param name="Maps">
///     The controller's static status map fields the action resolves with, or <see cref="SharedMap" />.
/// </param>
/// <param name="Messages">Each message constant, as <c>&lt;X&gt;Messages.&lt;Name&gt;</c>.</param>
/// <param name="Relayed">Whether the reached code builds a <c>HeimdallRelayedRefusal</c>.</param>
public sealed record HandlerMessages(
    IReadOnlyList<string> Handlers,
    IReadOnlyList<string> Maps,
    IReadOnlyList<string> Messages,
    bool Relayed)
{
    /// <summary>The map name for <see cref="FortunaStatusMap.Shared" />.</summary>
    public const string SharedMap = "Shared";
}

/// <summary>
///     Reads <c>OpenApi/handler-messages.json</c>, embedded in this assembly, and resolves an action's handler
///     error statuses through its real status maps.
/// </summary>
/// <remarks>
///     Which messages a handler fails with, and which map its action passes, are what the code does, not
///     metadata, so neither the running API nor the generator can work them out by reflection. The script reads
///     them from the source and scripts/openapi.py runs it before generating the document; its <c>--check</c>
///     fails when the committed file is out of date. The lookups themselves happen here, against the maps the
///     API uses, by the messages' text, as <c>ToActionResult</c> does.
/// </remarks>
public static class HandlerMessageCatalog
{
    public const string ResourceName = "ArturRios.Fortuna.WebApi.OpenApi.handler-messages.json";

    private static readonly Lazy<IReadOnlyDictionary<string, HandlerMessages>> Catalog = new(Load);

    private static readonly Lazy<IReadOnlyDictionary<string, Type>> MessageTypes = new(() =>
        new[] { typeof(UserProfileMessages).Assembly, typeof(HandlerMessageCatalog).Assembly }
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Name.EndsWith("Messages", StringComparison.Ordinal) && type.IsClass)
            .GroupBy(type => type.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal));

    /// <summary>Every action the catalog lists, by <c>&lt;Controller&gt;.&lt;Action&gt;</c>.</summary>
    public static IReadOnlyDictionary<string, HandlerMessages> Entries => Catalog.Value;

    /// <summary>
    ///     The text of a message constant (<c>BudgetMessages.ProfileNotFound</c>), or <see langword="null" /> for a
    ///     message a method builds, which no map holds.
    /// </summary>
    public static string? Text(string message)
    {
        var separator = message.IndexOf('.', StringComparison.Ordinal);

        return MessageTypes.Value.TryGetValue(message[..separator], out var type) &&
               type.GetField(message[(separator + 1)..], BindingFlags.Public | BindingFlags.Static) is
               { IsLiteral: true } field
            ? (string?)field.GetRawConstantValue()
            : null;
    }

    /// <summary>
    ///     The error statuses the action's handlers can answer: 400, which <c>ToActionResult</c> answers for a
    ///     failure its map does not hold (a validator's included), every status of 400 or more a map gives one of
    ///     the reached messages, and, where the code relays a Heimdall refusal, the statuses of the outcomes the
    ///     controller relays. And whether that relay carries <c>Retry-After</c> (its 429).
    /// </summary>
    public static (IReadOnlySet<int> Statuses, bool RelaysRetryAfter) Statuses(Type controller, string action)
    {
        var key = $"{controller.Name[..^"Controller".Length]}.{action}";
        if (!Entries.TryGetValue(key, out var entry))
        {
            throw new InvalidOperationException(
                $"{key} is not in {ResourceName}: run scripts/openapi.py to regenerate it.");
        }

        var statuses = new HashSet<int> { StatusCodes.Status400BadRequest };
        foreach (var map in entry.Maps.Select(name => Map(controller, name)))
        {
            foreach (var text in entry.Messages.Select(Text).OfType<string>())
            {
                if (map.TryGetValue(text, out var status) && status >= StatusCodes.Status400BadRequest)
                {
                    statuses.Add(status);
                }
            }
        }

        var relaysRetryAfter = false;
        if (entry.Relayed)
        {
            var relayed = RelayedStatuses(controller);
            statuses.UnionWith(relayed);
            relaysRetryAfter = relayed.Contains(StatusCodes.Status429TooManyRequests);
        }

        return (statuses, relaysRetryAfter);
    }

    /// <summary>The controller's static status map <paramref name="name" />, or the shared one.</summary>
    public static IReadOnlyDictionary<string, int> Map(Type controller, string name) =>
        name == HandlerMessages.SharedMap
            ? FortunaStatusMap.Shared
            : controller.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                  ?.GetValue(null) as IReadOnlyDictionary<string, int>
              ?? throw new InvalidOperationException($"{controller.Name} has no status map {name}.");

    /// <summary>
    ///     The statuses of 400 or more in the controller's maps keyed by something other than a message:
    ///     <c>AuthController</c>'s map of Heimdall's relayed outcomes.
    /// </summary>
    private static int[] RelayedStatuses(Type controller) =>
        controller.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(field => field.FieldType.IsGenericType &&
                            field.FieldType.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>) &&
                            field.FieldType.GetGenericArguments() is [var key, var value] &&
                            key != typeof(string) && value == typeof(int))
            .SelectMany(field => ((System.Collections.IDictionary)field.GetValue(null)!).Values.Cast<int>())
            .Where(status => status >= StatusCodes.Status400BadRequest)
            .ToArray();

    private static IReadOnlyDictionary<string, HandlerMessages> Load()
    {
        using var stream = typeof(HandlerMessageCatalog).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException(
                               $"{ResourceName} is not embedded in the Web API assembly. It is listed as an " +
                               "EmbeddedResource in ArturRios.Fortuna.WebApi.csproj and written by " +
                               "scripts/error_responses.py.");

        return JsonSerializer.Deserialize<Dictionary<string, HandlerMessages>>(
                   stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
               ?? throw new InvalidOperationException($"{ResourceName} is empty.");
    }
}
