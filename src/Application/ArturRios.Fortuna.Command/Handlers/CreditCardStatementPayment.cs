using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Messages;

namespace ArturRios.Fortuna.Command.Handlers;

/// <summary>
///     The one way a statement is paid, whether the caller settles the statement or records a
///     transfer whose destination is the statement: same store call, same refusal messages.
/// </summary>
internal static class CreditCardStatementPayment
{
    public static async Task<CreditCardStatementPaymentResult> PayAsync(
        ICreditCardStatementSettlementStore settlements,
        CreditCardStatementSettlement request)
    {
        var result = await settlements.SettleAsync(request, CancellationToken.None);

        return result.Outcome == CreditCardStatementSettlementOutcome.Succeeded &&
               result.Settlement is not null
            ? new CreditCardStatementPaymentResult(result.Settlement, null)
            : new CreditCardStatementPaymentResult(null, Refusal(result.Outcome));
    }

    private static string Refusal(CreditCardStatementSettlementOutcome outcome) => outcome switch
    {
        CreditCardStatementSettlementOutcome.StatementNotFound => CreditCardStatementMessages.NotFound,
        CreditCardStatementSettlementOutcome.FinancialAccountNotFound =>
            CreditCardStatementMessages.FinancialAccountNotFound,
        CreditCardStatementSettlementOutcome.StatementOpen => CreditCardStatementMessages.StatementOpen,
        CreditCardStatementSettlementOutcome.StatementAlreadySettled =>
            CreditCardStatementMessages.StatementAlreadySettled,
        CreditCardStatementSettlementOutcome.ExchangeRateUnavailable =>
            CreditCardStatementMessages.ExchangeRateUnavailable,
        _ => throw new InvalidOperationException("Unknown statement settlement outcome.")
    };
}

internal sealed record CreditCardStatementPaymentResult(
    CreditCardStatementSettlementSnapshot? Settlement,
    string? Error);
