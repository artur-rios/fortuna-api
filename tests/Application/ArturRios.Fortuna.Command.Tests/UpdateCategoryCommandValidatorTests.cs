using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class UpdateCategoryCommandValidatorTests
{
    private readonly UpdateCategoryCommandValidator validator = new();

    [UnitFact]
    public void GivenValidCategory_WhenValidated_ThenNoErrorsAreReturned()
    {
        var result = validator.Validate(new UpdateCategoryCommand
        {
            Id = Guid.NewGuid(),
            Name = "Dining",
            ParentId = Guid.NewGuid()
        });

        Assert.True(result.IsValid);
    }

    [UnitFact]
    public void GivenRootMove_WhenValidated_ThenNullParentIsAccepted()
    {
        var result = validator.Validate(new UpdateCategoryCommand
        {
            Id = Guid.NewGuid(),
            Name = "Dining",
            ParentId = null
        });

        Assert.True(result.IsValid);
    }

    [UnitFact]
    public void GivenInvalidFields_WhenValidated_ThenEveryErrorIsReturned()
    {
        var result = validator.Validate(new UpdateCategoryCommand
        {
            Id = Guid.Empty,
            Name = string.Empty,
            ParentId = Guid.Empty
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage == CategoryMessages.NotFound);
        Assert.Contains(result.Errors, error => error.ErrorMessage == CategoryMessages.NameRequired);
        Assert.Contains(result.Errors, error => error.ErrorMessage == CategoryMessages.ParentIdInvalid);
    }

    [UnitFact]
    public void GivenTooLongName_WhenValidated_ThenExpectedErrorIsReturned()
    {
        var result = validator.Validate(new UpdateCategoryCommand
        {
            Id = Guid.NewGuid(),
            Name = new string('n', 201)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage == CategoryMessages.NameTooLong);
    }
}
