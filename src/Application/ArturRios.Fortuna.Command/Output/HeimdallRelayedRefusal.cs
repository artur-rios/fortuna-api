using System.Text.Json.Serialization;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Output;

/// <summary>
///     A refusal Heimdall gave that Fortuna passes on as Heimdall answered it: a rate limit (with
///     its <c>Retry-After</c>) or a conflict. The envelope's error is Heimdall's own message, so the
///     response status cannot come from the message map; the controller reads it from
///     <see cref="Outcome" /> instead. Neither property is serialized.
/// </summary>
public interface IHeimdallRelayedRefusal
{
    HeimdallAuthOutcome Outcome { get; }

    int? RetryAfterSeconds { get; }
}

/// <inheritdoc cref="IHeimdallRelayedRefusal" />
public sealed class HeimdallRelayedRefusal<T> : DataOutput<T>, IHeimdallRelayedRefusal
{
    [JsonIgnore]
    public HeimdallAuthOutcome Outcome { get; init; }

    [JsonIgnore]
    public int? RetryAfterSeconds { get; init; }
}
