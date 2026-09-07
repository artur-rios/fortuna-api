using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class DrillIntoAggregationQueryValidatorTests
{
    private readonly DrillIntoAggregationQueryValidator validator = new();

    [UnitFact]
    public void GivenValidKeyAndDimension_WhenValidated_ThenInputIsAccepted()
    {
        var result = validator.Validate(new DrillIntoAggregationQuery
        {
            Key = "protected-key",
            Dimension = " Category ",
            PageNumber = 1,
            PageSize = 20
        });

        Assert.True(result.IsValid);
    }

    [UnitTheory]
    [InlineData("key", "unknown", 1, 20, "Dimension")]
    [InlineData("", null, 1, 20, TransactionDrillDownMessages.KeyRequired)]
    [InlineData("key", null, 0, 20, TransactionDrillDownMessages.InvalidPageNumber)]
    [InlineData("key", null, 1, 0, TransactionDrillDownMessages.InvalidPageSize)]
    public void GivenInvalidInput_WhenValidated_ThenExpectedErrorReturns(
        string key,
        string? dimension,
        int pageNumber,
        int pageSize,
        string expected)
    {
        var result = validator.Validate(new DrillIntoAggregationQuery
        {
            Key = key,
            Dimension = dimension,
            PageNumber = pageNumber,
            PageSize = pageSize
        });

        Assert.Contains(result.Errors,
            error => error.ErrorMessage.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }
}
