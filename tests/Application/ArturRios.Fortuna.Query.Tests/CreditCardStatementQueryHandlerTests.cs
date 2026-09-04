using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class CreditCardStatementQueryHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 21, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedStatement_WhenReadById_ThenSummaryAndTransactionsAreReturned()
    {
        var profile = Profile();
        var card = Card(profile.Id);
        var statement = Statement(card.Id, CreditCardStatementStatus.Closed, 125.50m);
        statement.Transactions.Add(Transaction(
            125.50m,
            isLateArriving: true,
            originalAmount: 25m,
            originalCurrencyCode: "USD",
            appliedRate: 5.02m));

        var result = await GetHandler(profile, new StubStatementReader(profile.Id, statement)).HandleAsync(
            new GetCreditCardStatementByIdQuery { Id = statement.Id });

        Assert.True(result.Success);
        Assert.Equal(statement.Id, result.Data?.Id);
        Assert.Equal("Closed", result.Data?.Status);
        Assert.Equal(125.50m, result.Data?.PurchaseTotal);
        var charge = Assert.Single(result.Data!.Transactions);
        Assert.Equal("Expense", charge.Direction);
        Assert.True(charge.IsLateArriving);
        Assert.Equal(25m, charge.OriginalAmount);
        Assert.Equal("USD", charge.OriginalCurrencyCode);
        Assert.Equal(5.02m, charge.AppliedRate);
        Assert.Contains(CreditCardStatementMessages.RetrievedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenMissingOrForeignStatement_WhenReadById_ThenNotFoundIsReturned()
    {
        var profile = Profile();
        var foreign = Statement(Guid.NewGuid());
        var reader = new StubStatementReader(Guid.NewGuid(), foreign);
        var handler = GetHandler(profile, reader);

        var foreignResult = await handler.HandleAsync(
            new GetCreditCardStatementByIdQuery { Id = foreign.Id });
        var missingResult = await handler.HandleAsync(
            new GetCreditCardStatementByIdQuery { Id = Guid.NewGuid() });

        Assert.Contains(CreditCardStatementMessages.NotFound, foreignResult.Errors);
        Assert.Equal(foreignResult.Errors, missingResult.Errors);
    }

    [UnitFact]
    public async Task GivenStatusAndPeriodFilters_WhenListed_ThenMatchingStatementsAreSorted()
    {
        var profile = Profile();
        var card = Card(profile.Id);
        var august = Statement(
            card.Id,
            CreditCardStatementStatus.Closed,
            10m,
            new DateOnly(2026, 7, 21));
        var september = Statement(
            card.Id,
            CreditCardStatementStatus.Closed,
            30m,
            new DateOnly(2026, 8, 21));
        var open = Statement(
            card.Id,
            CreditCardStatementStatus.Open,
            50m,
            new DateOnly(2026, 9, 21));
        var handler = ListHandler(
            profile,
            new StubCreditCardReader(card),
            new StubStatementReader(profile.Id, august, september, open));

        var result = await handler.HandleAsync(new ListCreditCardStatementsQuery
        {
            CreditCardId = card.Id,
            Status = CreditCardStatementStatus.Closed,
            From = new DateOnly(2026, 7, 1),
            To = new DateOnly(2026, 9, 20),
            SortBy = "AmountDue",
            Descending = true,
            PageNumber = 1,
            PageSize = 10
        });

        Assert.True(result.Success);
        Assert.Equal(2, result.TotalItems);
        Assert.Equal([30m, 10m], result.Data!.Select(statement => statement.AmountDue));
        Assert.Contains(CreditCardStatementMessages.ListedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenInvalidListCriteria_WhenListed_ThenEveryInvalidFieldIsReported()
    {
        var result = await ListHandler(
            null,
            new StubCreditCardReader(),
            new StubStatementReader(Guid.NewGuid())).HandleAsync(new ListCreditCardStatementsQuery
            {
                PageNumber = 0,
                PageSize = 0,
                Status = (CreditCardStatementStatus)999,
                From = new DateOnly(2026, 10, 1),
                To = new DateOnly(2026, 9, 1),
                SortBy = "Balance"
            });

        Assert.False(result.Success);
        Assert.Contains(CreditCardStatementMessages.InvalidPageNumber, result.Errors);
        Assert.Contains(CreditCardStatementMessages.InvalidPageSize, result.Errors);
        Assert.Contains(CreditCardStatementMessages.StatusInvalid, result.Errors);
        Assert.Contains(CreditCardStatementMessages.PeriodInvalid, result.Errors);
        Assert.Contains(CreditCardStatementMessages.SortByUnsupported, result.Errors);
    }

    [UnitFact]
    public async Task GivenUnknownProfileOrCard_WhenListed_ThenNotFoundIsReturned()
    {
        var profile = Profile();
        var unknownProfile = await ListHandler(
            null,
            new StubCreditCardReader(),
            new StubStatementReader(Guid.NewGuid())).HandleAsync(new ListCreditCardStatementsQuery());
        var unknownCard = await ListHandler(
            profile,
            new StubCreditCardReader(),
            new StubStatementReader(profile.Id)).HandleAsync(new ListCreditCardStatementsQuery
            {
                CreditCardId = Guid.NewGuid()
            });

        Assert.Contains(CreditCardStatementMessages.ProfileNotFound, unknownProfile.Errors);
        Assert.Contains(CreditCardStatementMessages.CreditCardNotFound, unknownCard.Errors);
    }

    [UnitFact]
    public async Task GivenOversizedPage_WhenListed_ThenConfiguredMaximumIsUsed()
    {
        var profile = Profile();
        var card = Card(profile.Id);
        var handler = ListHandler(
            profile,
            new StubCreditCardReader(card),
            new StubStatementReader(
                profile.Id,
                Statement(card.Id),
                Statement(card.Id, periodStart: new DateOnly(2026, 8, 21))),
            maximumPageSize: 1);

        var result = await handler.HandleAsync(new ListCreditCardStatementsQuery
        {
            CreditCardId = card.Id,
            PageSize = 500
        });

        Assert.Equal(1, result.PageSize);
        Assert.Equal(2, result.TotalItems);
        Assert.Single(result.Data!);
    }

    private static GetCreditCardStatementByIdQueryHandler GetHandler(
        UserProfileSnapshot? profile,
        ICreditCardStatementReader statements) => new(
        new StubUserProfileReader(profile),
        statements,
        Actor(profile));

    private static ListCreditCardStatementsQueryHandler ListHandler(
        UserProfileSnapshot? profile,
        ICreditCardReader cards,
        ICreditCardStatementReader statements,
        int maximumPageSize = 100) => new(
        new ListCreditCardStatementsQueryValidator(),
        new StubUserProfileReader(profile),
        cards,
        statements,
        Actor(profile),
        new PaginationOptions(maximumPageSize));

    private static StubActorAccessor Actor(UserProfileSnapshot? profile) => new(
        new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, []));

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Account Owner",
        "BRL",
        false,
        Now,
        Now);

    private static CreditCardLimitSnapshot Card(Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Name = "Rewards",
        Issuer = "Example Bank",
        CurrencyCode = "BRL",
        CreditLimit = 1000m,
        ClosingDay = 20,
        DueDay = 5,
        CreatedAt = Now,
        UpdatedAt = Now
    };

    private static CreditCardStatementReadSnapshot Statement(
        Guid creditCardId,
        CreditCardStatementStatus status = CreditCardStatementStatus.Open,
        decimal amountDue = 0m,
        DateOnly? periodStart = null)
    {
        var start = periodStart ?? new DateOnly(2026, 8, 21);
        return new CreditCardStatementReadSnapshot
        {
            Id = Guid.NewGuid(),
            CreditCardId = creditCardId,
            CurrencyCode = "BRL",
            PeriodStart = start,
            PeriodEnd = start.AddMonths(1).AddDays(-1),
            ClosingDate = start.AddMonths(1).AddDays(-1),
            DueDate = start.AddMonths(1).AddDays(14),
            PurchaseTotal = amountDue,
            AmountDue = amountDue,
            Status = status,
            CreatedAt = Now,
            UpdatedAt = Now
        };
    }

    private static CreditCardStatementTransactionSnapshot Transaction(
        decimal amount,
        bool isLateArriving = false,
        decimal? originalAmount = null,
        string? originalCurrencyCode = null,
        decimal? appliedRate = null) => new()
        {
            Id = Guid.NewGuid(),
            Direction = TransactionDirection.Expense,
            Amount = amount,
            OccurredOn = new DateOnly(2026, 9, 1),
            IsLateArriving = isLateArriving,
            OriginalAmount = originalAmount,
            OriginalCurrencyCode = originalCurrencyCode,
            AppliedRate = appliedRate,
            RateDate = appliedRate.HasValue ? new DateOnly(2026, 8, 31) : null,
            CreatedAt = Now,
            UpdatedAt = Now
        };

    private sealed class StubStatementReader(
        Guid ownerId,
        params CreditCardStatementReadSnapshot[] statements) : ICreditCardStatementReader
    {
        public IQueryable<CreditCardStatementReadSnapshot> Query(Guid userId) =>
            (userId == ownerId ? statements : []).AsQueryable();

        public Task<CreditCardStatementReadSnapshot?> FindByIdAsync(
            Guid userId,
            Guid statementId,
            CancellationToken cancellationToken) => Task.FromResult(userId == ownerId
            ? statements.SingleOrDefault(statement => statement.Id == statementId)
            : null);
    }

    private sealed class StubCreditCardReader(params CreditCardLimitSnapshot[] cards)
        : ICreditCardReader
    {
        public IQueryable<CreditCardLimitSnapshot> QueryLimits() => cards.AsQueryable();

        public Task<CreditCardLimitSnapshot?> FindByIdWithLimitsAsync(
            Guid userId,
            Guid id,
            CancellationToken cancellationToken) => Task.FromResult(cards.SingleOrDefault(card =>
                card.UserId == userId && card.Id == id && !card.IsDeleted));
    }

    private sealed class StubUserProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject,
            CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId,
            CancellationToken cancellationToken) => Task.FromResult(profile);
    }

    private sealed class StubActorAccessor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }
}
