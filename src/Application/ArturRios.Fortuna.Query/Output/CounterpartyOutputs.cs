using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class CounterpartyOutput : QueryOutput
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CounterpartyListOutput : QueryOutput
{
    public IReadOnlyCollection<CounterpartyOutput> Counterparties { get; set; } = [];
}

public sealed class CounterpartyCategorySuggestionOutput : QueryOutput
{
    public Guid CounterpartyId { get; set; }
    public bool HasSuggestion { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
}
