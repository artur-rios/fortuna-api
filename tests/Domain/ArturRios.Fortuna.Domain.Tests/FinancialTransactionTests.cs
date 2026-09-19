using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class FinancialTransactionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public void GivenOwnedImportedRecord_WhenReconciledThenUnreconciled_ThenLinkTracksState()
    {
        var user = User();
        var transaction = new FinancialTransaction(
            user,
            Account(user),
            Category(user),
            TransactionDirection.Expense,
            25m,
            new DateOnly(2026, 9, 3),
            Now);
        var record = ImportedRecord(user);

        transaction.Reconcile(record, Now.AddMinutes(1));

        Assert.True(transaction.IsReconciled);
        Assert.Equal(record, transaction.ImportedRecord);
        Assert.Equal(Now.AddMinutes(1), transaction.UpdatedAt);

        transaction.Unreconcile(Now.AddMinutes(2));

        Assert.False(transaction.IsReconciled);
        Assert.Null(transaction.ImportedRecord);
        Assert.Null(transaction.ImportedRecordId);
        Assert.Equal(Now.AddMinutes(2), transaction.UpdatedAt);
    }

    [UnitFact]
    public void GivenAlreadyReconciledTransaction_WhenReconciledAgain_ThenItIsRejected()
    {
        var user = User();
        var transaction = new FinancialTransaction(
            user,
            Account(user),
            Category(user),
            TransactionDirection.Expense,
            25m,
            new DateOnly(2026, 9, 3),
            Now);
        transaction.Reconcile(ImportedRecord(user), Now);

        Assert.Throws<InvalidOperationException>(() =>
            transaction.Reconcile(ImportedRecord(user), Now));
    }

    [UnitFact]
    public void GivenForeignImportedRecord_WhenReconciled_ThenItIsRejected()
    {
        var user = User();
        var transaction = new FinancialTransaction(
            user,
            Account(user),
            Category(user),
            TransactionDirection.Expense,
            25m,
            new DateOnly(2026, 9, 3),
            Now);

        Assert.Throws<ArgumentException>(() =>
            transaction.Reconcile(ImportedRecord(User()), Now));
    }

    [UnitFact]
    public void GivenRecordedTransaction_WhenUnreconciled_ThenItIsRejected()
    {
        var user = User();
        var transaction = new FinancialTransaction(
            user,
            Account(user),
            Category(user),
            TransactionDirection.Expense,
            25m,
            new DateOnly(2026, 9, 3),
            Now);

        Assert.Throws<InvalidOperationException>(() => transaction.Unreconcile(Now));
    }

    [UnitFact]
    public void GivenValidMovement_WhenCreated_ThenAccountLedgerFieldsAreFixed()
    {
        var user = User();
        var account = Account(user);
        var occurredOn = new DateOnly(2026, 9, 3);

        var transaction = new FinancialTransaction(
            user,
            account,
            Category(user),
            TransactionDirection.Expense,
            12.3456m,
            occurredOn,
            Now);

        Assert.Equal(user, transaction.User);
        Assert.Equal(account, transaction.FinancialAccount);
        Assert.Equal(TransactionDirection.Expense, transaction.Direction);
        Assert.Equal(12.3456m, transaction.Amount);
        Assert.Equal(occurredOn, transaction.OccurredOn);
        Assert.False(transaction.IsDeleted);
        Assert.Null(transaction.CreditCard);
    }

    [UnitFact]
    public void GivenValidCardCharge_WhenCreated_ThenCardLedgerFieldsAreFixed()
    {
        var user = User();
        var card = Card(user);
        var occurredOn = new DateOnly(2026, 9, 3);

        var transaction = new FinancialTransaction(
            user,
            card,
            Category(user),
            TransactionDirection.Expense,
            99.99m,
            occurredOn,
            Now);

        Assert.Equal(user, transaction.User);
        Assert.Equal(card, transaction.CreditCard);
        Assert.Null(transaction.FinancialAccount);
        Assert.Equal(TransactionDirection.Expense, transaction.Direction);
        Assert.Equal(99.99m, transaction.Amount);
        Assert.Equal(occurredOn, transaction.OccurredOn);
    }

    [UnitFact]
    public void GivenOptionalClassification_WhenCreated_ThenDetailsAndTagsAreRetained()
    {
        var user = User();
        var category = Category(user);
        var counterparty = new Counterparty(user, "Cafe", Now);
        var tag = new Tag(user, "Food", Now);

        var transaction = new FinancialTransaction(
            user,
            Account(user),
            category,
            TransactionDirection.Expense,
            10m,
            new DateOnly(2026, 9, 4),
            Now,
            "  Lunch  ",
            counterparty,
            [tag, tag]);

        Assert.Equal(category, transaction.Category);
        Assert.Equal(counterparty, transaction.Counterparty);
        Assert.Equal("Lunch", transaction.Description);
        Assert.Equal(user.DisplayCurrency.Code, transaction.Currency.Code);
        Assert.Single(transaction.Tags);
        Assert.Equal(TransactionSourceType.Manual, transaction.SourceType);
        Assert.False(transaction.IsReconciled);
    }

    [UnitFact]
    public void GivenForeignClassification_WhenCreated_ThenItIsRejected()
    {
        var user = User();
        var foreign = User();

        Assert.Throws<ArgumentException>(() => new FinancialTransaction(
            user,
            Account(user),
            Category(foreign),
            TransactionDirection.Expense,
            10m,
            new DateOnly(2026, 9, 4),
            Now));
        Assert.Throws<ArgumentException>(() => new FinancialTransaction(
            user,
            Account(user),
            Category(user),
            TransactionDirection.Expense,
            10m,
            new DateOnly(2026, 9, 4),
            Now,
            counterparty: new Counterparty(foreign, "Foreign", Now)));
        Assert.Throws<ArgumentException>(() => new FinancialTransaction(
            user,
            Account(user),
            Category(user),
            TransactionDirection.Expense,
            10m,
            new DateOnly(2026, 9, 4),
            Now,
            tags: [new Tag(foreign, "Foreign", Now)]));
    }

    [UnitFact]
    public void GivenDifferentCardOwner_WhenCreated_ThenTransactionIsRejected()
    {
        var owner = User();
        var other = User();

        var exception = Assert.Throws<ArgumentException>(() => new FinancialTransaction(
            other,
            Card(owner),
            Category(other),
            TransactionDirection.Expense,
            1m,
            new DateOnly(2026, 9, 4),
            Now));

        Assert.Equal("card", exception.ParamName);
    }

    [UnitFact]
    public void GivenDifferentOwner_WhenCreated_ThenTransactionIsRejected()
    {
        var owner = User();
        var other = User();

        var exception = Assert.Throws<ArgumentException>(() => new FinancialTransaction(
            other,
            Account(owner),
            Category(other),
            TransactionDirection.Earning,
            1m,
            new DateOnly(2026, 9, 4),
            Now));

        Assert.Equal("account", exception.ParamName);
    }

    [UnitTheory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GivenNonPositiveAmount_WhenCreated_ThenTransactionIsRejected(decimal amount)
    {
        var user = User();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new FinancialTransaction(
            user,
            Account(user),
            Category(user),
            TransactionDirection.Earning,
            amount,
            new DateOnly(2026, 9, 4),
            Now));

        Assert.Equal("amount", exception.ParamName);
    }

    [UnitFact]
    public void GivenUnknownDirection_WhenCreated_ThenTransactionIsRejected()
    {
        var user = User();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new FinancialTransaction(
            user,
            Account(user),
            Category(user),
            (TransactionDirection)999,
            1m,
            new DateOnly(2026, 9, 4),
            Now));

        Assert.Equal("direction", exception.ParamName);
    }

    [UnitFact]
    public void GivenForeignCurrencyCharge_WhenDetailsRecorded_ThenConversionIsRetained()
    {
        var user = User();
        var transaction = new FinancialTransaction(
            user,
            Card(user),
            Category(user),
            TransactionDirection.Expense,
            125.50m,
            new DateOnly(2026, 9, 4),
            Now);
        var usd = new Currency("USD", "US dollar", 2);
        var rateDate = new DateOnly(2026, 9, 3);

        transaction.RecordForeignCurrencyDetails(25m, usd, 5.02m, rateDate, Now.AddMinutes(1));

        Assert.Equal(25m, transaction.OriginalAmount);
        Assert.Equal(usd, transaction.OriginalCurrency);
        Assert.Equal(5.02m, transaction.AppliedRate);
        Assert.Equal(rateDate, transaction.RateDate);
        Assert.Equal(Now.AddMinutes(1), transaction.UpdatedAt);
    }

    [UnitFact]
    public void GivenBilledCurrencyAsOriginal_WhenDetailsRecorded_ThenItIsRejected()
    {
        var user = User();
        var transaction = new FinancialTransaction(
            user,
            Card(user),
            Category(user),
            TransactionDirection.Expense,
            10m,
            new DateOnly(2026, 9, 4),
            Now);

        var exception = Assert.Throws<ArgumentException>(() =>
            transaction.RecordForeignCurrencyDetails(
                10m,
                Currency(),
                1m,
                new DateOnly(2026, 9, 4),
                Now));

        Assert.Equal("originalCurrency", exception.ParamName);
    }

    [UnitTheory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public void GivenNonPositiveConversionFigure_WhenDetailsRecorded_ThenItIsRejected(
        decimal originalAmount,
        decimal appliedRate)
    {
        var user = User();
        var transaction = new FinancialTransaction(
            user,
            Card(user),
            Category(user),
            TransactionDirection.Expense,
            10m,
            new DateOnly(2026, 9, 4),
            Now);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            transaction.RecordForeignCurrencyDetails(
                originalAmount,
                new Currency("USD", "US dollar", 2),
                appliedRate,
                new DateOnly(2026, 9, 4),
                Now));
    }

    [UnitFact]
    public void GivenValidChanges_WhenUpdated_ThenEditableFieldsAndTimestampChange()
    {
        var user = User();
        var transaction = new FinancialTransaction(
            user,
            Account(user),
            Category(user),
            TransactionDirection.Expense,
            10m,
            new DateOnly(2026, 9, 3),
            Now,
            "Before",
            tags: [new Tag(user, "Old", Now)]);
        var category = new Category(user, "Dining", Now);
        var counterparty = new Counterparty(user, "Cafe", Now);
        var tag = new Tag(user, "Food", Now);
        var changedAt = Now.AddHours(1);

        transaction.UpdateDetails(
            category,
            TransactionDirection.Earning,
            25m,
            new DateOnly(2026, 9, 4),
            "  After  ",
            counterparty,
            [tag, tag],
            changedAt);

        Assert.Equal(category, transaction.Category);
        Assert.Equal(counterparty, transaction.Counterparty);
        Assert.Equal(TransactionDirection.Earning, transaction.Direction);
        Assert.Equal(25m, transaction.Amount);
        Assert.Equal(new DateOnly(2026, 9, 4), transaction.OccurredOn);
        Assert.Equal("After", transaction.Description);
        Assert.Equal([tag], transaction.Tags);
        Assert.Equal(changedAt, transaction.UpdatedAt);
        Assert.False(transaction.IsManuallyCorrected);
    }

    [UnitFact]
    public void GivenForeignCurrencyCharge_WhenAmountEdited_ThenStaleConversionIsCleared()
    {
        var user = User();
        var transaction = new FinancialTransaction(
            user,
            Card(user),
            Category(user),
            TransactionDirection.Expense,
            125.50m,
            new DateOnly(2026, 9, 4),
            Now);
        transaction.RecordForeignCurrencyDetails(
            25m,
            new Currency("USD", "US dollar", 2),
            5.02m,
            new DateOnly(2026, 9, 3),
            Now);

        transaction.UpdateDetails(
            transaction.Category,
            transaction.Direction,
            130m,
            transaction.OccurredOn,
            null,
            null,
            null,
            Now.AddMinutes(1));

        Assert.Equal(130m, transaction.Amount);
        Assert.Null(transaction.OriginalAmount);
        Assert.Null(transaction.OriginalCurrency);
        Assert.Null(transaction.AppliedRate);
        Assert.Null(transaction.RateDate);
    }

    [UnitFact]
    public void GivenForeignCurrencyCharge_WhenOnlyDescriptionEdited_ThenConversionIsKept()
    {
        var user = User();
        var transaction = new FinancialTransaction(
            user,
            Card(user),
            Category(user),
            TransactionDirection.Expense,
            125.50m,
            new DateOnly(2026, 9, 4),
            Now);
        transaction.RecordForeignCurrencyDetails(
            25m,
            new Currency("USD", "US dollar", 2),
            5.02m,
            new DateOnly(2026, 9, 3),
            Now);

        transaction.UpdateDetails(
            transaction.Category,
            transaction.Direction,
            125.50m,
            transaction.OccurredOn,
            "Hotel",
            null,
            null,
            Now.AddMinutes(1));

        Assert.Equal(25m, transaction.OriginalAmount);
        Assert.Equal(5.02m, transaction.AppliedRate);
    }

    [UnitFact]
    public void GivenImportedTransaction_WhenUpdated_ThenItIsMarkedManuallyCorrected()
    {
        var user = User();
        var transaction = new FinancialTransaction(
            user,
            Account(user),
            Category(user),
            TransactionDirection.Expense,
            10m,
            new DateOnly(2026, 9, 4),
            Now);
        typeof(FinancialTransaction)
            .GetProperty(nameof(FinancialTransaction.SourceType))!
            .SetValue(transaction, TransactionSourceType.Excel);

        transaction.UpdateDetails(
            Category(user),
            TransactionDirection.Expense,
            10m,
            new DateOnly(2026, 9, 4),
            "Corrected",
            null,
            [],
            Now.AddHours(1));

        Assert.Equal(TransactionSourceType.Excel, transaction.SourceType);
        Assert.True(transaction.IsManuallyCorrected);
    }

    [UnitFact]
    public void GivenForeignClassification_WhenUpdated_ThenItIsRejected()
    {
        var owner = User();
        var foreign = User();
        var transaction = new FinancialTransaction(
            owner,
            Account(owner),
            Category(owner),
            TransactionDirection.Expense,
            10m,
            new DateOnly(2026, 9, 4),
            Now);

        Assert.Throws<ArgumentException>(() => transaction.UpdateDetails(
            Category(foreign),
            TransactionDirection.Expense,
            10m,
            new DateOnly(2026, 9, 4),
            null,
            null,
            [],
            Now.AddHours(1)));
        Assert.Throws<ArgumentException>(() => transaction.UpdateDetails(
            Category(owner),
            TransactionDirection.Expense,
            10m,
            new DateOnly(2026, 9, 4),
            null,
            new Counterparty(foreign, "Foreign", Now),
            [],
            Now.AddHours(1)));
    }

    [UnitFact]
    public void GivenOwnedTag_WhenAttachedAndDetached_ThenAssignmentIsIdempotent()
    {
        var user = User();
        var transaction = new FinancialTransaction(
            user,
            Account(user),
            Category(user),
            TransactionDirection.Expense,
            10m,
            new DateOnly(2026, 9, 4),
            Now);
        var tag = new Tag(user, "Food", Now);

        Assert.True(transaction.AttachTag(tag, Now.AddMinutes(1)));
        Assert.False(transaction.AttachTag(tag, Now.AddMinutes(2)));
        Assert.Single(transaction.Tags);
        Assert.True(transaction.DetachTag(tag, Now.AddMinutes(3)));
        Assert.False(transaction.DetachTag(tag, Now.AddMinutes(4)));
        Assert.Empty(transaction.Tags);
        Assert.Equal(Now.AddMinutes(3), transaction.UpdatedAt);
    }

    [UnitFact]
    public void GivenForeignTag_WhenAttached_ThenItIsRejected()
    {
        var user = User();
        var transaction = new FinancialTransaction(
            user,
            Account(user),
            Category(user),
            TransactionDirection.Expense,
            10m,
            new DateOnly(2026, 9, 4),
            Now);

        Assert.Throws<ArgumentException>(() =>
            transaction.AttachTag(new Tag(User(), "Foreign", Now), Now));
    }

    [UnitFact]
    public void GivenDeletedReferences_WhenTransactionCreated_ThenEachIsRejected()
    {
        var user = User();
        var deletedAccount = Account(user);
        deletedAccount.SoftDelete(Now);
        var deletedCategory = Category(user);
        deletedCategory.SoftDelete(Now);
        var deletedCounterparty = new Counterparty(user, "Shop", Now);
        deletedCounterparty.SoftDelete(Now);
        var deletedTag = new Tag(user, "Old", Now);
        deletedTag.SoftDelete(Now);

        Assert.Throws<ArgumentException>(() => new FinancialTransaction(
            user, deletedAccount, Category(user), TransactionDirection.Expense, 1m,
            new DateOnly(2026, 9, 4), Now));
        Assert.Throws<ArgumentException>(() => new FinancialTransaction(
            user, Account(user), deletedCategory, TransactionDirection.Expense, 1m,
            new DateOnly(2026, 9, 4), Now));
        Assert.Throws<ArgumentException>(() => new FinancialTransaction(
            user, Account(user), Category(user), TransactionDirection.Expense, 1m,
            new DateOnly(2026, 9, 4), Now, counterparty: deletedCounterparty));
        Assert.Throws<ArgumentException>(() => new FinancialTransaction(
            user, Account(user), Category(user), TransactionDirection.Expense, 1m,
            new DateOnly(2026, 9, 4), Now, tags: [deletedTag]));
    }

    [UnitFact]
    public void GivenDeletedTransaction_WhenEdited_ThenEveryMutationIsRefused()
    {
        var user = User();
        var transaction = new FinancialTransaction(
            user,
            Account(user),
            Category(user),
            TransactionDirection.Expense,
            25m,
            new DateOnly(2026, 9, 3),
            Now);
        transaction.SoftDelete(Now);

        Assert.Throws<InvalidOperationException>(() => transaction.UpdateDetails(
            Category(user), TransactionDirection.Expense, 30m, new DateOnly(2026, 9, 3),
            null, null, null, Now));
        Assert.Throws<InvalidOperationException>(() =>
            transaction.AttachTag(new Tag(user, "Food", Now), Now));
        Assert.Throws<InvalidOperationException>(() =>
            transaction.Reconcile(ImportedRecord(user), Now));
        Assert.Equal(25m, transaction.Amount);
    }

    [UnitFact]
    public void GivenDeletedTag_WhenAttached_ThenItIsRejected()
    {
        var user = User();
        var transaction = new FinancialTransaction(
            user,
            Account(user),
            Category(user),
            TransactionDirection.Expense,
            25m,
            new DateOnly(2026, 9, 3),
            Now);
        var tag = new Tag(user, "Old", Now);
        tag.SoftDelete(Now);

        Assert.Throws<ArgumentException>(() => transaction.AttachTag(tag, Now));
        Assert.Empty(transaction.Tags);
    }

    [UnitFact]
    public void GivenDeletedStatement_WhenChargeAssigned_ThenItIsRejected()
    {
        var user = User();
        var card = Card(user);
        var charge = new FinancialTransaction(
            user,
            card,
            Category(user),
            TransactionDirection.Expense,
            25m,
            new DateOnly(2026, 9, 3),
            Now);
        var statement = new CreditCardStatement(
            card,
            BillingCycle.Containing(new DateOnly(2026, 9, 3), card.ClosingDay, card.DueDay),
            Now);
        statement.SoftDelete(Now);

        Assert.Throws<ArgumentException>(() => charge.AssignToStatement(statement, false, Now));
        Assert.Null(charge.Statement);
    }

    private static UserProfile User() => new(
        Guid.NewGuid(),
        "Account Owner",
        Currency(),
        Now);

    private static Category Category(UserProfile user) => new(user, "General", Now);

    private static FinancialAccount Account(UserProfile user) => new(
        user,
        "Daily",
        null,
        FinancialAccountType.Checking,
        Currency(),
        0m,
        Now);

    private static CreditCard Card(UserProfile user) => new(
        user,
        "Rewards",
        "Example Bank",
        Currency(),
        1000m,
        20,
        5,
        null,
        Now);

    private static ImportedRecord ImportedRecord(UserProfile user) => new(
        new ImportJob(user, TransactionSourceType.Excel, Now),
        "{\"amount\":25,\"occurredOn\":\"2026-09-03\"}",
        ImportedRecordOutcome.Imported,
        25m,
        new DateOnly(2026, 9, 3));

    private static Currency Currency() => new("BRL", "Brazilian real", 2);
}
