using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class AttachDocumentCommand : BaseCommand
{
    [JsonIgnore]
    public Guid TransactionId { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;

    [JsonIgnore]
    public byte[] Content { get; set; } = [];
}
