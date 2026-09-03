namespace ArturRios.Fortuna.Data.Configuration;

public sealed record DatabaseDiagnosticsOptions(bool SensitiveDataLogging, bool DetailedErrors)
{
    public static DatabaseDiagnosticsOptions Disabled { get; } = new(false, false);
}
