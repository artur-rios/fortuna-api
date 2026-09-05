using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class GetTransferByIdQueryValidatorTests
{
    [UnitFact]
    public async Task GivenTransferId_WhenValidated_ThenItIsAccepted()
    {
        var result = await new GetTransferByIdQueryValidator().ValidateAsync(
            new GetTransferByIdQuery { Id = Guid.NewGuid() });

        Assert.True(result.IsValid);
    }

    [UnitFact]
    public async Task GivenEmptyTransferId_WhenValidated_ThenItIsRejected()
    {
        var result = await new GetTransferByIdQueryValidator().ValidateAsync(
            new GetTransferByIdQuery());

        Assert.Contains(result.Errors, error =>
            error.ErrorMessage == TransferMessages.TransferIdRequired);
    }
}
