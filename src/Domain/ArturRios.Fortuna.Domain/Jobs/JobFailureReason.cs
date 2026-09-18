namespace ArturRios.Fortuna.Domain.Jobs;

/// <summary>Normalizes the failure reason every job-like entity records when it fails.</summary>
public static class JobFailureReason
{
    public const int MaximumLength = 1000;
    public const string Default = "The job failed.";

    public static string Normalize(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Default;
        }

        var trimmed = reason.Trim();

        return trimmed.Length <= MaximumLength ? trimmed : trimmed[..MaximumLength];
    }
}
