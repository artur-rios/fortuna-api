using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class CreateCategoryCommandValidatorTests
{
    private readonly CreateCategoryCommandValidator validator = new();

    [UnitFact]
    public async Task GivenValidRootCategory_WhenValidated_ThenItIsAccepted()
    {
        var result = await validator.ValidateAsync(new CreateCategoryCommand { Name = "Dining" });

        Assert.True(result.IsValid);
    }

    [UnitTheory]
    [InlineData("", CategoryMessages.NameRequired)]
    [InlineData("   ", CategoryMessages.NameRequired)]
    public async Task GivenMissingName_WhenValidated_ThenRequiredErrorIsReturned(
        string name,
        string expected)
    {
        var result = await validator.ValidateAsync(new CreateCategoryCommand { Name = name });

        Assert.Contains(result.Errors, failure => failure.ErrorMessage == expected);
    }

    [UnitFact]
    public async Task GivenNameLongerThanLimit_WhenValidated_ThenLengthErrorIsReturned()
    {
        var result = await validator.ValidateAsync(new CreateCategoryCommand
        {
            Name = new string('c', 201)
        });

        Assert.Contains(result.Errors, failure =>
            failure.ErrorMessage == CategoryMessages.NameTooLong);
    }

    [UnitFact]
    public async Task GivenEmptyParentId_WhenValidated_ThenIdentifierErrorIsReturned()
    {
        var result = await validator.ValidateAsync(new CreateCategoryCommand
        {
            Name = "Dining",
            ParentId = Guid.Empty
        });

        Assert.Contains(result.Errors, failure =>
            failure.ErrorMessage == CategoryMessages.ParentIdInvalid);
    }
}
