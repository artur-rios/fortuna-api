using System.Linq.Expressions;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Cards;

namespace ArturRios.Fortuna.Query.Handlers;

internal static class CreditCardProjection
{
    public static readonly Expression<Func<CreditCardLimitSnapshot, CreditCardOutput>>
        Expression = card => new CreditCardOutput
        {
            Id = card.Id,
            Name = card.Name,
            Issuer = card.Issuer,
            CurrencyCode = card.CurrencyCode,
            CreditLimit = card.CreditLimit,
            UsedAmount = Math.Max(card.OutstandingAmount, 0m),
            AvailableAmount = Math.Max(
                card.CreditLimit - Math.Max(card.OutstandingAmount, 0m),
                0m),
            OverageAmount = Math.Max(
                Math.Max(card.OutstandingAmount, 0m) - card.CreditLimit,
                0m),
            ClosingDay = card.ClosingDay,
            DueDay = card.DueDay,
            LastFourDigits = card.LastFourDigits,
            IsDeleted = card.IsDeleted,
            CreatedAt = card.CreatedAt,
            UpdatedAt = card.UpdatedAt
        };

    private static readonly Func<CreditCardLimitSnapshot, CreditCardOutput> Compiled =
        Expression.Compile();

    public static CreditCardOutput From(CreditCardLimitSnapshot card) => Compiled(card);
}
