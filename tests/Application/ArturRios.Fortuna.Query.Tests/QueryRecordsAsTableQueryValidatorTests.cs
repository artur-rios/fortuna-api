using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class QueryRecordsAsTableQueryValidatorTests
{
    private readonly QueryRecordsAsTableQueryValidator validator = new();

    [UnitFact]
    public async Task GivenValidStructuredCriteria_WhenValidated_ThenItIsAccepted()
    {
        var result = await validator.ValidateAsync(new QueryRecordsAsTableQuery
        {
            RecordSet = "transactions",
            Columns = ["id", "amount"],
            Filters = [new() { Field = "amount", Operator = "gte", Value = "10" }],
            Sorts = [new() { Field = "occurredOn", Descending = true }],
            DisplayCurrencyCode = "BRL",
            PageNumber = 1,
            PageSize = 100
        });

        Assert.True(result.IsValid);
    }

    [UnitFact]
    public async Task GivenMissingDuplicateOrInvalidCriteria_WhenValidated_ThenEveryFieldIsNamed()
    {
        var result = await validator.ValidateAsync(new QueryRecordsAsTableQuery
        {
            RecordSet = string.Empty,
            Columns = ["id", " ID "],
            Filters = [new() { Field = string.Empty, Operator = string.Empty, Value = null! }],
            Sorts = [new() { Field = string.Empty }],
            DisplayCurrencyCode = "US",
            PageNumber = 0,
            PageSize = 0
        });
        var errors = result.Errors.Select(error => error.ErrorMessage).ToArray();

        Assert.Contains(TableReportMessages.RecordSetRequired, errors);
        Assert.Contains(TableReportMessages.ColumnsMustBeUnique, errors);
        Assert.Contains(TableReportMessages.FilterFieldRequired, errors);
        Assert.Contains(TableReportMessages.FilterOperatorRequired, errors);
        Assert.Contains(TableReportMessages.FilterValueRequired, errors);
        Assert.Contains(TableReportMessages.SortFieldRequired, errors);
        Assert.Contains(TableReportMessages.DisplayCurrencyInvalid, errors);
        Assert.Contains(TableReportMessages.InvalidPageNumber, errors);
        Assert.Contains(TableReportMessages.InvalidPageSize, errors);
    }

    [UnitFact]
    public async Task GivenNoColumns_WhenValidated_ThenColumnsAreRequired()
    {
        var result = await validator.ValidateAsync(new QueryRecordsAsTableQuery
        {
            RecordSet = "transactions",
            Columns = [],
            PageNumber = 1,
            PageSize = 1
        });

        Assert.Contains(result.Errors,
            error => error.ErrorMessage == TableReportMessages.ColumnsRequired);
    }
}
