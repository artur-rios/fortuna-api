using System.Text.Json.Serialization;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class ConvertFigureQuery : BaseQuery
{
    public string? DisplayCurrencyCode { get; set; }
    public DateOnly FigureDate { get; set; }
    public IReadOnlyCollection<FigureAmountInput>? Amounts { get; set; } = [];

    [JsonIgnore]
    public Guid ExternalSubject { get; set; }

    [JsonIgnore]
    public bool IsLocal { get; set; }
}

public sealed class FigureAmountInput
{
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
}
