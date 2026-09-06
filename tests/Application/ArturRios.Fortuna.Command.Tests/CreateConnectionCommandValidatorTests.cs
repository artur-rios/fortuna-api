using System.Text.Json;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class CreateConnectionCommandValidatorTests
{
    [UnitFact]
    public async Task GivenValidReference_WhenValidated_ThenItIsAccepted()
    {
        var result = await new CreateConnectionCommandValidator().ValidateAsync(
            new CreateConnectionCommand
            {
                DataSource = " Pluggy ",
                ExternalReference = Guid.NewGuid().ToString()
            });

        Assert.True(result.IsValid);
    }

    [UnitFact]
    public async Task GivenMissingOrInvalidFields_WhenValidated_ThenTheyAreRejected()
    {
        var result = await new CreateConnectionCommandValidator().ValidateAsync(
            new CreateConnectionCommand
            {
                DataSource = "excel",
                ExternalReference = "not-an-item-id"
            });

        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == ConnectionMessages.DataSourceInvalid);
        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == ConnectionMessages.ExternalReferenceInvalid);
    }

    [UnitFact]
    public async Task GivenBankCredentialField_WhenValidated_ThenItIsRejected()
    {
        var result = await new CreateConnectionCommandValidator().ValidateAsync(
            new CreateConnectionCommand
            {
                DataSource = "pluggy",
                ExternalReference = Guid.NewGuid().ToString(),
                AdditionalFields = new Dictionary<string, JsonElement>
                {
                    ["password"] = JsonDocument.Parse("\"secret\"").RootElement.Clone()
                }
            });

        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == ConnectionMessages.BankCredentialRejected);
    }
}
