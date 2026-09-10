using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ArturRios.Fortuna.WebApi.Serialization;

/// <summary>Preserves CLR decimal values across JSON transports without a binary float.</summary>
public sealed partial class ExactDecimalJsonConverter : JsonConverter<decimal>
{
    private const NumberStyles WireStyles =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    public override decimal Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var numeric))
        {
            return numeric;
        }
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            if (value is not null &&
                WirePattern().IsMatch(value) &&
                decimal.TryParse(value, WireStyles, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }
        throw new JsonException("The value must be an invariant decimal string.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        decimal value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));

    [GeneratedRegex(@"^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex WirePattern();
}
