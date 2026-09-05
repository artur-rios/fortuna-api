using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class ReassignCategoryTransactionsCommandValidatorTests
{
    private readonly ReassignCategoryTransactionsCommandValidator validator = new();

    [UnitFact]
    public void GivenDifferentCategories_WhenValidated_ThenNoErrorsAreReturned()
    {
        var result = validator.Validate(new ReassignCategoryTransactionsCommand
        {
            Id = Guid.NewGuid(),
            TargetCategoryId = Guid.NewGuid(),
            IncludeDescendants = true
        });

        Assert.True(result.IsValid);
    }

    [UnitFact]
    public void GivenMissingIdentifiers_WhenValidated_ThenBothErrorsAreReturned()
    {
        var result = validator.Validate(new ReassignCategoryTransactionsCommand());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage == CategoryMessages.NotFound);
        Assert.Contains(result.Errors, error =>
            error.ErrorMessage == CategoryMessages.TargetCategoryIdInvalid);
    }

    [UnitFact]
    public void GivenSameCategory_WhenValidated_ThenExpectedErrorIsReturned()
    {
        var id = Guid.NewGuid();

        var result = validator.Validate(new ReassignCategoryTransactionsCommand
        {
            Id = id,
            TargetCategoryId = id
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.ErrorMessage == CategoryMessages.SourceAndTargetMustDiffer);
    }
}
