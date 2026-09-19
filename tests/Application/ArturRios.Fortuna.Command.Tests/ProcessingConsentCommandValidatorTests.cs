using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class ProcessingConsentCommandValidatorTests
{
    private static readonly ProcessingConsentOptions Options = new("2026-09");

    [UnitFact]
    public async Task GivenCurrentVersion_WhenGrantValidated_ThenItIsAccepted()
    {
        var result = await new GrantProcessingConsentCommandValidator(Options).ValidateAsync(
            new GrantProcessingConsentCommand
            {
                Purpose = " External-Data-Processing ",
                Version = " 2026-09 "
            });

        Assert.True(result.IsValid);
    }

    [UnitTheory]
    [InlineData("marketing", "2026-09", ProcessingConsentMessages.UnknownPurpose)]
    [InlineData("external-data-processing", "", ProcessingConsentMessages.VersionRequired)]
    [InlineData("external-data-processing", "2025-01", ProcessingConsentMessages.VersionNotCurrent)]
    public async Task GivenInvalidGrant_WhenValidated_ThenOnlyTheMatchingErrorIsReported(
        string purpose,
        string version,
        string expected)
    {
        var result = await new GrantProcessingConsentCommandValidator(Options).ValidateAsync(
            new GrantProcessingConsentCommand { Purpose = purpose, Version = version });

        var error = Assert.Single(result.Errors);
        Assert.Equal(expected, error.ErrorMessage);
    }

    [UnitTheory]
    [InlineData("external-data-processing", true)]
    [InlineData("unknown", false)]
    [InlineData("", false)]
    public async Task GivenPurpose_WhenWithdrawalValidated_ThenOnlyKnownPurposesPass(
        string purpose,
        bool expected)
    {
        var result = await new WithdrawProcessingConsentCommandValidator().ValidateAsync(
            new WithdrawProcessingConsentCommand { Purpose = purpose });

        Assert.Equal(expected, result.IsValid);
    }

    [UnitTheory]
    [InlineData("pt-br", true, "pt-BR")]
    [InlineData(" en-US ", true, "en-US")]
    [InlineData("pt", false, "")]
    [InlineData("xx-YY", false, "")]
    [InlineData("invalid-locale", false, "")]
    public void GivenLocale_WhenResolvedForExport_ThenOnlyKnownSpecificCulturesResolve(
        string value,
        bool expected,
        string canonical)
    {
        var resolved = DataExportInput.TryResolveLocale(value, out var locale);

        Assert.Equal(expected, resolved);
        Assert.Equal(canonical, locale);
    }
}
