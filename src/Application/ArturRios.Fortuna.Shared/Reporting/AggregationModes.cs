using System.Text.Json.Serialization;

namespace ArturRios.Fortuna.Shared.Reporting;

[JsonConverter(typeof(JsonStringEnumConverter<AggregationDimension>))]
public enum AggregationDimension
{
    [JsonStringEnumMemberName("period")]
    Period = 1,

    [JsonStringEnumMemberName("category")]
    Category = 2,

    [JsonStringEnumMemberName("account")]
    Account = 3,

    [JsonStringEnumMemberName("card")]
    Card = 4,

    [JsonStringEnumMemberName("counterparty")]
    Counterparty = 5,

    [JsonStringEnumMemberName("tag")]
    Tag = 6
}

[JsonConverter(typeof(JsonStringEnumConverter<AggregationGranularity>))]
public enum AggregationGranularity
{
    [JsonStringEnumMemberName("day")]
    Day = 1,

    [JsonStringEnumMemberName("week")]
    Week = 2,

    [JsonStringEnumMemberName("month")]
    Month = 3,

    [JsonStringEnumMemberName("quarter")]
    Quarter = 4,

    [JsonStringEnumMemberName("year")]
    Year = 5
}

/// <summary>
/// Converts the wire names of aggregation dimensions and granularities to their enums once, at
/// the edge, so every later decision switches over a closed set of values.
/// </summary>
public static class AggregationModes
{
    private static readonly IReadOnlyDictionary<string, AggregationDimension> Dimensions =
        new Dictionary<string, AggregationDimension>(StringComparer.OrdinalIgnoreCase)
        {
            ["period"] = AggregationDimension.Period,
            ["category"] = AggregationDimension.Category,
            ["account"] = AggregationDimension.Account,
            ["card"] = AggregationDimension.Card,
            ["counterparty"] = AggregationDimension.Counterparty,
            ["tag"] = AggregationDimension.Tag
        };

    private static readonly IReadOnlyDictionary<string, AggregationGranularity> Granularities =
        new Dictionary<string, AggregationGranularity>(StringComparer.OrdinalIgnoreCase)
        {
            ["day"] = AggregationGranularity.Day,
            ["week"] = AggregationGranularity.Week,
            ["month"] = AggregationGranularity.Month,
            ["quarter"] = AggregationGranularity.Quarter,
            ["year"] = AggregationGranularity.Year
        };

    private static readonly IReadOnlyDictionary<AggregationDimension, string> DimensionNamesByValue =
        Dimensions.ToDictionary(item => item.Value, item => item.Key);

    private static readonly IReadOnlyDictionary<AggregationGranularity, string>
        GranularityNamesByValue = Granularities.ToDictionary(item => item.Value, item => item.Key);

    public static IReadOnlyCollection<string> DimensionNames { get; } = [.. Dimensions.Keys];

    public static IReadOnlyCollection<string> GranularityNames { get; } = [.. Granularities.Keys];

    public static bool TryParseDimension(string? value, out AggregationDimension dimension)
    {
        dimension = default;

        return value is not null && Dimensions.TryGetValue(value.Trim(), out dimension);
    }

    public static bool TryParseGranularity(string? value, out AggregationGranularity granularity)
    {
        granularity = default;

        return value is not null && Granularities.TryGetValue(value.Trim(), out granularity);
    }

    public static string Name(this AggregationDimension dimension) =>
        DimensionNamesByValue[dimension];

    public static string Name(this AggregationGranularity granularity) =>
        GranularityNamesByValue[granularity];
}
