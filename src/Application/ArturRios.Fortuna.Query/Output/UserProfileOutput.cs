using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class UserProfileOutput : QueryOutput
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string DisplayCurrency { get; set; } = string.Empty;
    public bool DisplayCurrencyRequiresConfirmation { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
