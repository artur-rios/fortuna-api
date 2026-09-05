using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class ReconcileTransactionCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public Guid? ImportJobId { get; set; }
    public long? ImportedRecordId { get; set; }
    public bool Unreconcile { get; set; }
}
