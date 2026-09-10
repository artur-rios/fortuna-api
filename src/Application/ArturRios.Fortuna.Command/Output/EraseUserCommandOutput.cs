using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class EraseUserCommandOutput : CommandOutput
{
    public IReadOnlyDictionary<string, int> Erased { get; set; } =
        new Dictionary<string, int>();
    public int RevokedConnections { get; set; }
    public bool Irreversible => true;
}
