using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class TagCommandValidatorTests
{
    [UnitTheory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GivenMissingName_WhenTagValidated_ThenItIsRejected(string name)
    {
        var create = await new CreateTagCommandValidator().ValidateAsync(
            new CreateTagCommand { Name = name });
        var update = await new UpdateTagCommandValidator().ValidateAsync(
            new UpdateTagCommand { Name = name });

        Assert.Contains(create.Errors, item => item.ErrorMessage == TagMessages.NameRequired);
        Assert.Contains(update.Errors, item => item.ErrorMessage == TagMessages.NameRequired);
    }

    [UnitFact]
    public async Task GivenLongName_WhenTagValidated_ThenItIsRejected()
    {
        var name = new string('t', 201);

        var create = await new CreateTagCommandValidator().ValidateAsync(
            new CreateTagCommand { Name = name });
        var update = await new UpdateTagCommandValidator().ValidateAsync(
            new UpdateTagCommand { Name = name });

        Assert.Contains(create.Errors, item => item.ErrorMessage == TagMessages.NameTooLong);
        Assert.Contains(update.Errors, item => item.ErrorMessage == TagMessages.NameTooLong);
    }

    [UnitFact]
    public async Task GivenValidNames_WhenTagValidated_ThenTheyAreAccepted()
    {
        var create = await new CreateTagCommandValidator().ValidateAsync(
            new CreateTagCommand { Name = "Food" });
        var update = await new UpdateTagCommandValidator().ValidateAsync(
            new UpdateTagCommand { Name = "Dining" });

        Assert.True(create.IsValid);
        Assert.True(update.IsValid);
    }

    [UnitFact]
    public async Task GivenEmptyIds_WhenAssignmentValidated_ThenTheyAreRejected()
    {
        var attach = await new AttachTransactionTagCommandValidator().ValidateAsync(
            new AttachTransactionTagCommand());
        var detach = await new DetachTransactionTagCommandValidator().ValidateAsync(
            new DetachTransactionTagCommand());

        Assert.Contains(attach.Errors, item => item.ErrorMessage == TagMessages.TransactionIdInvalid);
        Assert.Contains(attach.Errors, item => item.ErrorMessage == TagMessages.TagIdInvalid);
        Assert.Contains(detach.Errors, item => item.ErrorMessage == TagMessages.TransactionIdInvalid);
        Assert.Contains(detach.Errors, item => item.ErrorMessage == TagMessages.TagIdInvalid);
    }

    [UnitFact]
    public async Task GivenValidIds_WhenAssignmentValidated_ThenTheyAreAccepted()
    {
        var attach = await new AttachTransactionTagCommandValidator().ValidateAsync(
            new AttachTransactionTagCommand
            {
                Id = Guid.NewGuid(),
                TagId = Guid.NewGuid()
            });
        var detach = await new DetachTransactionTagCommandValidator().ValidateAsync(
            new DetachTransactionTagCommand
            {
                Id = Guid.NewGuid(),
                TagId = Guid.NewGuid()
            });

        Assert.True(attach.IsValid);
        Assert.True(detach.IsValid);
    }
}
