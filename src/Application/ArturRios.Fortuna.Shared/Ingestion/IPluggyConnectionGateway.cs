namespace ArturRios.Fortuna.Shared.Ingestion;

public interface IPluggyConnectionGateway
{
    Task<PluggyConnectionValidation> ValidateAsync(
        string externalReference,
        CancellationToken cancellationToken);
}

public enum PluggyConnectionValidationOutcome
{
    Succeeded = 1,
    InvalidReference = 2,
    Unavailable = 3,
    NotConfigured = 4
}

public sealed record PluggyConnectionValidation(
    PluggyConnectionValidationOutcome Outcome,
    string? Institution = null,
    string? AccessToken = null);
