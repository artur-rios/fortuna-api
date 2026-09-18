using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Attachments;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;

namespace ArturRios.Fortuna.Data.Tests;

/// <summary>
/// One owner's September 2026 with every kind of row the reporting read paths must agree on:
/// live expenses and an earning, a card charge, a transfer, a soft-deleted transaction, a live
/// transaction under a soft-deleted account, a deleted tag and another owner's data.
/// </summary>
internal sealed record ReportingScenario(
    Guid UserId,
    Guid CheckingId,
    Guid CardId,
    Guid FoodId,
    Guid GroceriesId,
    Guid LiveTagId,
    Guid DeletedTagId,
    Guid CounterpartyId,
    Guid GroceriesTransactionId)
{
    public static readonly DateOnly From = new(2026, 9, 1);
    public static readonly DateOnly To = new(2026, 9, 30);
    public static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-30T12:00:00Z");

    /// <summary>Live, non-transfer rows: groceries, salary, card coffee and the deleted-tag row.</summary>
    public const int ReportableCount = 4;

    public const decimal Expense = 18.75m;
    public const decimal Earning = 100m;

    public static async Task<ReportingScenario> SeedAsync(AppDbContext context)
    {
        var currency = new Currency("BRL", "Brazilian Real", 2);
        var user = new UserProfile(Guid.NewGuid(), "Owner", currency, Now);
        var other = new UserProfile(Guid.NewGuid(), "Other", currency, Now);
        context.AddRange(currency, user, other);
        await context.SaveChangesAsync();

        var checking = new FinancialAccount(
            user, "Checking", null, FinancialAccountType.Checking, currency, 0m, Now);
        var savings = new FinancialAccount(
            user, "Savings", null, FinancialAccountType.Savings, currency, 0m, Now);
        var closed = new FinancialAccount(
            user, "Closed", null, FinancialAccountType.Cash, currency, 0m, Now);
        var card = new CreditCard(user, "Card", "Bank", currency, 1000m, 5, 15, null, Now);
        var food = new Category(user, "Food", Now);
        var groceries = new Category(user, "Groceries", Now, food);
        var liveTag = new Tag(user, "Live", Now);
        var deletedTag = new Tag(user, "Gone", Now);
        var counterparty = new Counterparty(user, "Market", Now);
        var otherAccount = new FinancialAccount(
            other, "Other", null, FinancialAccountType.Checking, currency, 0m, Now);
        var otherCategory = new Category(other, "Other", Now);
        context.AddRange(
            checking, savings, closed, card, food, groceries, liveTag, deletedTag, counterparty,
            otherAccount, otherCategory);
        await context.SaveChangesAsync();

        var groceriesTransaction = new FinancialTransaction(
            user, checking, groceries, TransactionDirection.Expense, 10.50m,
            new DateOnly(2026, 9, 2), Now, "50% off_sale", counterparty, [liveTag]);
        var salary = new FinancialTransaction(
            user, checking, food, TransactionDirection.Earning, 100m,
            new DateOnly(2026, 9, 10), Now, "Salary");
        var coffee = new FinancialTransaction(
            user, card, food, TransactionDirection.Expense, 5.25m,
            new DateOnly(2026, 9, 14), Now, "Card coffee");
        var deletedTagRow = new FinancialTransaction(
            user, checking, food, TransactionDirection.Expense, 3m,
            new DateOnly(2026, 9, 29), Now, "Tagged with a deleted tag", tags: [deletedTag]);
        var ghost = new FinancialTransaction(
            user, closed, food, TransactionDirection.Expense, 1000m,
            new DateOnly(2026, 9, 3), Now, "Ghost under a closed account");
        var removed = new FinancialTransaction(
            user, checking, food, TransactionDirection.Expense, 7m,
            new DateOnly(2026, 9, 4), Now, "Removed");
        var outbound = new FinancialTransaction(
            user, checking, food, TransactionDirection.Expense, 30m,
            new DateOnly(2026, 9, 5), Now, "Transfer out");
        var inbound = new FinancialTransaction(
            user, savings, food, TransactionDirection.Earning, 30m,
            new DateOnly(2026, 9, 5), Now, "Transfer in");
        var foreign = new FinancialTransaction(
            other, otherAccount, otherCategory, TransactionDirection.Expense, 500m,
            new DateOnly(2026, 9, 2), Now, "50% foreign");
        context.AddRange(
            groceriesTransaction, salary, coffee, deletedTagRow, ghost, removed, outbound,
            inbound, foreign);
        await context.SaveChangesAsync();

        context.Add(new Transfer(outbound, inbound, null, null, Now));
        context.AddRange(
            new Attachment(groceriesTransaction, "b.pdf", "application/pdf", 10, "k/b", Now),
            new Attachment(groceriesTransaction, "a.pdf", "application/pdf", 20, "k/a", Now));
        removed.SoftDelete(Now);
        closed.SoftDelete(Now);
        deletedTag.SoftDelete(Now);
        await context.SaveChangesAsync();

        return new ReportingScenario(
            user.PublicId,
            checking.PublicId,
            card.PublicId,
            food.PublicId,
            groceries.PublicId,
            liveTag.PublicId,
            deletedTag.PublicId,
            counterparty.PublicId,
            groceriesTransaction.PublicId);
    }
}
