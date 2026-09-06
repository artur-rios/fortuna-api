using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class ImportPdfInvoiceCommand : BaseCommand
{
    [JsonIgnore]
    public string? CorrelationId { get; set; }

    public Guid CreditCardId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
}
