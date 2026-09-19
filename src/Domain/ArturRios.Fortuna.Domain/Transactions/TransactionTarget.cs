using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Users;

namespace ArturRios.Fortuna.Domain.Transactions;

/// <summary>
/// What a money movement is booked against: exactly one financial account or one credit card.
/// Shared by transactions, recurring rules and connection mappings so the exclusive-or rule and
/// the owner/currency derivation live in one place.
/// </summary>
internal readonly record struct TransactionTarget
{
    private TransactionTarget(FinancialAccount? account, CreditCard? card)
    {
        Account = account;
        Card = card;
    }

    public FinancialAccount? Account { get; }
    public CreditCard? Card { get; }
    public UserProfile Owner => Account?.User ?? Card!.User;
    public Currency Currency => Account?.Currency ?? Card!.Currency;
    public bool IsDeleted => Account?.IsDeleted ?? Card!.IsDeleted;

    public static TransactionTarget Of(
        FinancialAccount? account,
        CreditCard? card,
        string? parameterName = null,
        string message = "Exactly one transaction target is required.")
    {
        if ((account is null) == (card is null))
        {
            throw new ArgumentException(message, parameterName);
        }

        return new TransactionTarget(account, card);
    }
}
