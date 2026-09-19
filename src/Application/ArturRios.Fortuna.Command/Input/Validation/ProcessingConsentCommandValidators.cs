using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class GrantProcessingConsentCommandValidator
    : AbstractValidator<GrantProcessingConsentCommand>
{
    public GrantProcessingConsentCommandValidator(ProcessingConsentOptions options)
    {
        RuleFor(command => command.Purpose)
            .Must(purpose => ProcessingConsentOptions.TryParsePurpose(purpose, out _))
            .WithMessage(ProcessingConsentMessages.UnknownPurpose);
        RuleFor(command => command.Version)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(ProcessingConsentMessages.VersionRequired)
            .Must((command, version) => IsCurrentVersion(options, command.Purpose, version))
            .WithMessage(ProcessingConsentMessages.VersionNotCurrent);
    }

    // An unknown purpose is reported by its own rule; there is no version to compare it with.
    private static bool IsCurrentVersion(
        ProcessingConsentOptions options,
        string purposeName,
        string version) =>
        !ProcessingConsentOptions.TryParsePurpose(purposeName, out var purpose) ||
        string.Equals(version.Trim(), options.CurrentVersion(purpose), StringComparison.Ordinal);
}

public sealed class WithdrawProcessingConsentCommandValidator
    : AbstractValidator<WithdrawProcessingConsentCommand>
{
    public WithdrawProcessingConsentCommandValidator() => RuleFor(command => command.Purpose)
        .Must(purpose => ProcessingConsentOptions.TryParsePurpose(purpose, out _))
        .WithMessage(ProcessingConsentMessages.UnknownPurpose);
}
