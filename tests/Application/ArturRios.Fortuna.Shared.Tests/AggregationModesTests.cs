using System.Text.Json;
using ArturRios.Fortuna.Shared.Reporting;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Shared.Tests;

public sealed class AggregationModesTests
{
    [UnitTheory]
    [InlineData("period", AggregationDimension.Period)]
    [InlineData(" Category ", AggregationDimension.Category)]
    [InlineData("ACCOUNT", AggregationDimension.Account)]
    [InlineData("card", AggregationDimension.Card)]
    [InlineData("counterparty", AggregationDimension.Counterparty)]
    [InlineData("tag", AggregationDimension.Tag)]
    public void GivenSupportedDimensionName_WhenParsed_ThenEnumAndWireNameRoundTrip(
        string value,
        AggregationDimension expected)
    {
        var parsed = AggregationModes.TryParseDimension(value, out var dimension);

        Assert.True(parsed);
        Assert.Equal(expected, dimension);
        Assert.Equal(value.Trim().ToLowerInvariant(), dimension.Name());
    }

    [UnitTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("merchant")]
    public void GivenUnknownDimensionName_WhenParsed_ThenParsingFailsWithoutThrowing(string? value)
    {
        Assert.False(AggregationModes.TryParseDimension(value, out _));
    }

    [UnitTheory]
    [InlineData("day", AggregationGranularity.Day)]
    [InlineData(" Week", AggregationGranularity.Week)]
    [InlineData("month", AggregationGranularity.Month)]
    [InlineData("QUARTER", AggregationGranularity.Quarter)]
    [InlineData("year", AggregationGranularity.Year)]
    public void GivenSupportedGranularityName_WhenParsed_ThenEnumAndWireNameRoundTrip(
        string value,
        AggregationGranularity expected)
    {
        var parsed = AggregationModes.TryParseGranularity(value, out var granularity);

        Assert.True(parsed);
        Assert.Equal(expected, granularity);
        Assert.Equal(value.Trim().ToLowerInvariant(), granularity.Name());
    }

    [UnitFact]
    public void GivenUnknownGranularityName_WhenParsed_ThenParsingFailsWithoutThrowing()
    {
        Assert.False(AggregationModes.TryParseGranularity("fortnight", out _));
    }

    [UnitFact]
    public void GivenDrillDownPayload_WhenSerialized_ThenModesKeepTheirLowercaseWireNames()
    {
        var selection = new TransactionAggregationSelection(
            AggregationDimension.Counterparty,
            "none",
            false);

        var json = JsonSerializer.Serialize(selection);
        var roundTrip = JsonSerializer.Deserialize<TransactionAggregationSelection>(
            """{"Dimension":"period","Value":"x","RollupCategories":false}""");

        Assert.Contains("\"Dimension\":\"counterparty\"", json, StringComparison.Ordinal);
        Assert.Equal(AggregationDimension.Period, roundTrip!.Dimension);
    }
}
