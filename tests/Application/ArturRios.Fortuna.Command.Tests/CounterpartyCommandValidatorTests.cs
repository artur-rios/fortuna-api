using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class CounterpartyCommandValidatorTests
{
    [UnitTheory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GivenMissingName_WhenCounterpartyValidated_ThenItIsRejected(string name)
    {
        var create = await new CreateCounterpartyCommandValidator().ValidateAsync(
            new CreateCounterpartyCommand { Name = name });
        var update = await new UpdateCounterpartyCommandValidator().ValidateAsync(
            new UpdateCounterpartyCommand { Name = name });

        Assert.Contains(create.Errors, item =>
            item.ErrorMessage == CounterpartyMessages.NameRequired);
        Assert.Contains(update.Errors, item =>
            item.ErrorMessage == CounterpartyMessages.NameRequired);
    }

    [UnitFact]
    public async Task GivenLongName_WhenCounterpartyValidated_ThenItIsRejected()
    {
        var name = new string('c', 201);

        var create = await new CreateCounterpartyCommandValidator().ValidateAsync(
            new CreateCounterpartyCommand { Name = name });
        var update = await new UpdateCounterpartyCommandValidator().ValidateAsync(
            new UpdateCounterpartyCommand { Name = name });

        Assert.Contains(create.Errors, item =>
            item.ErrorMessage == CounterpartyMessages.NameTooLong);
        Assert.Contains(update.Errors, item =>
            item.ErrorMessage == CounterpartyMessages.NameTooLong);
    }

    [UnitFact]
    public async Task GivenValidNames_WhenCounterpartyValidated_ThenTheyAreAccepted()
    {
        var create = await new CreateCounterpartyCommandValidator().ValidateAsync(
            new CreateCounterpartyCommand { Name = "Corner Cafe" });
        var update = await new UpdateCounterpartyCommandValidator().ValidateAsync(
            new UpdateCounterpartyCommand { Name = "Corner Market" });

        Assert.True(create.IsValid);
        Assert.True(update.IsValid);
    }

    [UnitFact]
    public async Task GivenEmptyTarget_WhenMergeValidated_ThenItIsRejected()
    {
        var result = await new MergeCounterpartiesCommandValidator().ValidateAsync(
            new MergeCounterpartiesCommand());

        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == CounterpartyMessages.TargetIdInvalid);
    }

    [UnitFact]
    public async Task GivenTarget_WhenMergeValidated_ThenItIsAccepted()
    {
        var result = await new MergeCounterpartiesCommandValidator().ValidateAsync(
            new MergeCounterpartiesCommand { TargetId = Guid.NewGuid() });

        Assert.True(result.IsValid);
    }
}
