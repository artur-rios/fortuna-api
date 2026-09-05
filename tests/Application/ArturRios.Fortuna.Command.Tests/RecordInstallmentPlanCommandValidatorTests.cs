using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class RecordInstallmentPlanCommandValidatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public void GivenValidPurchase_WhenValidated_ThenItPasses()
    {
        var result = Validator().Validate(Command());

        Assert.True(result.IsValid);
    }

    [UnitFact]
    public void GivenInvalidPurchase_WhenValidated_ThenEveryInvalidFieldIsReported()
    {
        var command = new RecordInstallmentPlanCommand
        {
            TotalAmount = 0m,
            InstallmentCount = 1,
            CurrencyCode = "US",
            Counterparty = new string('x', 201),
            OwnerId = Guid.NewGuid()
        };

        var result = Validator().Validate(command);

        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == InstallmentPlanMessages.CreditCardIdRequired);
        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == InstallmentPlanMessages.CategoryIdRequired);
        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == InstallmentPlanMessages.TotalAmountPositive);
        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == InstallmentPlanMessages.InstallmentCountMinimum);
        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == InstallmentPlanMessages.PurchasedOnRequired);
        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == InstallmentPlanMessages.CurrencyCodeInvalid);
        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == InstallmentPlanMessages.CounterpartyTooLong);
        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == InstallmentPlanMessages.OwnerImmutable);
    }

    private static RecordInstallmentPlanCommandValidator Validator() =>
        new(new FixedTimeProvider(Now));

    private static RecordInstallmentPlanCommand Command() => new()
    {
        CreditCardId = Guid.NewGuid(),
        CategoryId = Guid.NewGuid(),
        TotalAmount = 100m,
        InstallmentCount = 3,
        PurchasedOn = new DateOnly(2026, 9, 5),
        CurrencyCode = "USD",
        Counterparty = "Shop"
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
