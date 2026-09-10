namespace ArturRios.Fortuna.WebApi.Output;

public sealed class LivenessOutput
{
    public required string ContractVersion { get; init; }
    public required string Service { get; init; }
}
