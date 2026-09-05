using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class ReassignCategoryTransactionsCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public Guid TargetCategoryId { get; set; }
    public bool IncludeDescendants { get; set; }
    public int ReassignedCount { get; set; }
}
