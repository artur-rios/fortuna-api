using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Jwt;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class InvestmentMovementRecordingTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenEveryMovementType_WhenRecorded_ThenPositionAndAuditAreUpdated()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var investmentId = await SeedInvestmentAsync(client, subject, "BRL");
        var today = Today();

        var contribution = await PostAsync(
            client, investmentId, InvestmentMovementType.Contribution, 100m, today);
        var withdrawal = await PostAsync(
            client, investmentId, InvestmentMovementType.Withdrawal, 30m, today);
        var investmentYield = await PostAsync(
            client, investmentId, InvestmentMovementType.Yield, 10m, today);
        var fee = await PostAsync(client, investmentId, InvestmentMovementType.Fee, 5m, today);

        Assert.Equal(100m, contribution.Data!.Position);
        Assert.Equal(70m, withdrawal.Data!.Position);
        Assert.Equal(80m, investmentYield.Data!.Position);
        Assert.Equal(75m, fee.Data!.Position);
        Assert.All(
            new[] { contribution, withdrawal, investmentYield, fee },
            envelope => Assert.Equal("BRL", envelope.Data!.CurrencyCode));
        await using var context = CreateContext();
        Assert.Equal(4, await context.InvestmentMovements.CountAsync(item =>
            item.Investment.PublicId == investmentId));
        var audits = await context.AuditEntries.Where(item =>
            item.Operation == "RecordInvestmentMovementCommand" &&
            item.Outcome == AuditOutcome.Succeeded).ToArrayAsync();
        Assert.Equal(4, audits.Length);
        var movementIds = new[]
        {
            contribution.Data.Id,
            withdrawal.Data.Id,
            investmentYield.Data.Id,
            fee.Data.Id
        };
        Assert.All(audits, audit =>
        {
            Assert.True(audit.EntityPublicId.HasValue);
            Assert.Contains(audit.EntityPublicId.Value, movementIds);
        });
        Assert.All(audits, audit => Assert.Equal("InvestmentMovement", audit.EntityType));
    }

    [FunctionalFact]
    public async Task GivenInvalidValues_WhenRecorded_ThenBadRequestStoresNothing()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var investmentId = await SeedInvestmentAsync(client, subject, "BRL");

        var zero = await client.PostAsJsonAsync(
            $"/api/investments/{investmentId}/movements",
            Command(InvestmentMovementType.Contribution, 0m, Today()));
        var negative = await client.PostAsJsonAsync(
            $"/api/investments/{investmentId}/movements",
            Command(InvestmentMovementType.Contribution, -1m, Today()));
        var future = await client.PostAsJsonAsync(
            $"/api/investments/{investmentId}/movements",
            Command(InvestmentMovementType.Contribution, 1m, Today().AddDays(2)));

        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        Assert.Contains(
            InvestmentMessages.MovementAmountPositive,
            await zero.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Contains(
            InvestmentMessages.OccurredOnTooFarInFuture,
            await future.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.False(await context.InvestmentMovements.AnyAsync(item =>
            item.Investment.PublicId == investmentId));
    }

    [FunctionalFact]
    public async Task GivenDeletedOrForeignInvestment_WhenRecorded_ThenNotFoundIsReturned()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var ownerClient = factory.CreateClient();
        using var otherClient = factory.CreateClient();
        Authorize(ownerClient, owner, HeimdallRoles.User);
        Authorize(otherClient, other, HeimdallRoles.User);
        var deletedId = await SeedInvestmentAsync(ownerClient, owner, "BRL", true);
        var liveId = await SeedInvestmentAsync(ownerClient, owner, "BRL");

        var deleted = await ownerClient.PostAsJsonAsync(
            $"/api/investments/{deletedId}/movements",
            Command(InvestmentMovementType.Contribution, 10m, Today()));
        await EnsureProfileAsync(otherClient);
        var foreign = await otherClient.PostAsJsonAsync(
            $"/api/investments/{liveId}/movements",
            Command(InvestmentMovementType.Contribution, 10m, Today()));

        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.InvestmentMovements.AnyAsync(item =>
            item.Investment.PublicId == deletedId || item.Investment.PublicId == liveId));
    }

    [FunctionalFact]
    public async Task GivenSameCurrencyFunding_WhenContributed_ThenTransferOutflowIsStored()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var investmentId = await SeedInvestmentAsync(client, subject, "BRL");
        var accountId = await SeedAccountAsync(client, subject, "BRL");

        var envelope = await PostAsync(
            client,
            investmentId,
            InvestmentMovementType.Contribution,
            250m,
            Today(),
            accountId);

        Assert.Equal(250m, envelope.Data!.Amount);
        Assert.Equal(250m, envelope.Data.FundingAmount);
        Assert.Equal("BRL", envelope.Data.FundingCurrencyCode);
        Assert.Null(envelope.Data.AppliedRate);
        await using var context = CreateContext();
        var transfer = await context.Transfers
            .Include(item => item.OutboundTransaction)
            .ThenInclude(item => item.FinancialAccount)
            .Include(item => item.InboundInvestmentMovement)
            .SingleAsync(item => item.PublicId == envelope.Data.TransferId);
        Assert.Null(transfer.InboundTransactionId);
        Assert.Equal(envelope.Data.Id, transfer.InboundInvestmentMovement!.PublicId);
        Assert.Equal(TransactionDirection.Expense, transfer.OutboundTransaction.Direction);
        Assert.Equal(250m, transfer.OutboundTransaction.Amount);
        Assert.Equal(accountId, transfer.OutboundTransaction.FinancialAccount!.PublicId);
    }

    [FunctionalFact]
    public async Task GivenCrossCurrencyFunding_WhenContributed_ThenRecordedRateConvertsAmount()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var investmentId = await SeedInvestmentAsync(client, subject, "BRL");
        var accountId = await SeedAccountAsync(client, subject, "USD");
        await SeedRateAsync("USD", "BRL", 5m, Today());

        var envelope = await PostAsync(
            client,
            investmentId,
            InvestmentMovementType.Contribution,
            100m,
            Today(),
            accountId);

        Assert.Equal(500m, envelope.Data!.Amount);
        Assert.Equal(500m, envelope.Data.Position);
        Assert.Equal(100m, envelope.Data.FundingAmount);
        Assert.Equal("USD", envelope.Data.FundingCurrencyCode);
        Assert.Equal(5m, envelope.Data.AppliedRate);
        Assert.Equal(Today(), envelope.Data.RateDate);
        await using var context = CreateContext();
        var transfer = await context.Transfers.SingleAsync(item =>
            item.PublicId == envelope.Data.TransferId);
        Assert.Equal(5m, transfer.AppliedRate);
        Assert.Equal(Today(), transfer.RateDate);
    }

    [FunctionalFact]
    public async Task GivenMissingRate_WhenContributed_ThenConflictIsAtomic()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var investmentId = await SeedInvestmentAsync(client, subject, "BRL");
        var accountId = await SeedAccountAsync(client, subject, "USD");

        var response = await client.PostAsJsonAsync(
            $"/api/investments/{investmentId}/movements",
            Command(InvestmentMovementType.Contribution, 100m, Today(), accountId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            InvestmentMessages.ExchangeRateUnavailable,
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.False(await context.InvestmentMovements.AnyAsync(item =>
            item.Investment.PublicId == investmentId));
        Assert.False(await context.Transfers.AnyAsync(item =>
            item.OutboundTransaction.FinancialAccount!.PublicId == accountId));
        Assert.False(await context.FinancialTransactions.AnyAsync(item =>
            item.FinancialAccount!.PublicId == accountId));
    }

    [FunctionalFact]
    public async Task GivenFundingForNonContribution_WhenRecorded_ThenBadRequestIsReturned()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var investmentId = await SeedInvestmentAsync(client, subject, "BRL");
        var accountId = await SeedAccountAsync(client, subject, "BRL");

        var response = await client.PostAsJsonAsync(
            $"/api/investments/{investmentId}/movements",
            Command(InvestmentMovementType.Withdrawal, 10m, Today(), accountId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            InvestmentMessages.FundingRequiresContribution,
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.False(await context.InvestmentMovements.AnyAsync(item =>
            item.Investment.PublicId == investmentId));
    }

    [FunctionalFact]
    public async Task GivenUnauthorizedActor_WhenRecorded_ThenNothingIsStored()
    {
        var owner = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var ownerClient = factory.CreateClient();
        Authorize(ownerClient, owner, HeimdallRoles.User);
        var investmentId = await SeedInvestmentAsync(ownerClient, owner, "BRL");
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        var command = Command(InvestmentMovementType.Contribution, 10m, Today());

        var anonymousResponse = await anonymous.PostAsJsonAsync(
            $"/api/investments/{investmentId}/movements", command);
        var administratorResponse = await administrator.PostAsJsonAsync(
            $"/api/investments/{investmentId}/movements", command);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorResponse.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.InvestmentMovements.AnyAsync(item =>
            item.Investment.PublicId == investmentId));
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private async Task<Guid> SeedInvestmentAsync(
        HttpClient client,
        Guid subject,
        string currencyCode,
        bool deleted = false)
    {
        await EnsureProfileAsync(client);
        await using var context = CreateContext();
        var user = await context.UserProfiles.SingleAsync(item =>
            item.ExternalSubject == subject.ToString("D"));
        var currency = await context.Currencies.SingleAsync(item => item.Code == currencyCode);
        var investment = new Investment(
            user,
            $"Fund {Guid.NewGuid():N}",
            "Broker",
            InvestmentType.Fund,
            currency,
            DateTimeOffset.UtcNow);
        if (deleted)
        {
            investment.SoftDelete(DateTimeOffset.UtcNow);
        }

        context.Investments.Add(investment);
        await context.SaveChangesAsync();
        return investment.PublicId;
    }

    private async Task<Guid> SeedAccountAsync(
        HttpClient client,
        Guid subject,
        string currencyCode)
    {
        await EnsureProfileAsync(client);
        await using var context = CreateContext();
        var user = await context.UserProfiles.SingleAsync(item =>
            item.ExternalSubject == subject.ToString("D"));
        var currency = await context.Currencies.SingleAsync(item => item.Code == currencyCode);
        var account = new FinancialAccount(
            user,
            $"Account {Guid.NewGuid():N}",
            "Bank",
            FinancialAccountType.Checking,
            currency,
            1000m,
            DateTimeOffset.UtcNow);
        context.FinancialAccounts.Add(account);
        await context.SaveChangesAsync();
        return account.PublicId;
    }

    private async Task SeedRateAsync(
        string baseCurrencyCode,
        string quoteCurrencyCode,
        decimal rate,
        DateOnly rateDate)
    {
        await using var context = CreateContext();
        var currencies = await context.Currencies.Where(item =>
            item.Code == baseCurrencyCode || item.Code == quoteCurrencyCode).ToArrayAsync();
        context.ExchangeRates.Add(new ExchangeRate(
            currencies.Single(item => item.Code == baseCurrencyCode).Id,
            currencies.Single(item => item.Code == quoteCurrencyCode).Id,
            rate,
            rateDate,
            ExchangeRateSource.Manual));
        await context.SaveChangesAsync();
    }

    private static async Task EnsureProfileAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<MovementEnvelope> PostAsync(
        HttpClient client,
        Guid investmentId,
        InvestmentMovementType movementType,
        decimal amount,
        DateOnly occurredOn,
        Guid? accountId = null)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/investments/{investmentId}/movements",
            Command(movementType, amount, occurredOn, accountId));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return (await response.Content.ReadFromJsonAsync<MovementEnvelope>())!;
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        foreach (var setting in ValidSettings())
        {
            Environment.SetEnvironmentVariable(setting.Key, setting.Value);
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<AppDbContext>();
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options =>
                    options.UseNpgsql(database.GetConnectionString()));
            });
        });
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(database.GetConnectionString())
            .Options;
        return new AppDbContext(
            options,
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            DatabaseDiagnosticsOptions.Disabled);
    }

    private static object Command(
        InvestmentMovementType movementType,
        decimal amount,
        DateOnly occurredOn,
        Guid? financialAccountId = null) => new
        {
            MovementType = movementType,
            Amount = amount,
            OccurredOn = occurredOn,
            FinancialAccountId = financialAccountId
        };

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Account Owner"
        };
        var configuration = new JwtConfiguration(
            3600,
            Issuer,
            Audience,
            Secret,
            new FortunaIdentityMapper().ToClaims(identity));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            new JwtHandler().CreateToken(configuration));
    }

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["FORTUNA_DATA_CONNECTIONSTRING"] =
            "Host=localhost;Database=fortuna;Username=postgres;Password=postgres;Search Path=fortuna",
        ["FORTUNA_DATA_DATABASETYPE"] = "PostgreSql",
        ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
        ["FORTUNA_STORAGE_PATH"] = Path.Combine(Path.GetTempPath(), "fortuna-api-tests"),
        ["FORTUNA_LOG_DIRECTORY"] = Path.Combine(Path.GetTempPath(), "fortuna-api-test-logs"),
        ["FORTUNA_JOB_QUEUE_CAPACITY"] = "32",
        ["FORTUNA_AUTH_TOKEN_SECRET"] = Secret,
        ["FORTUNA_AUTH_TOKEN_ISSUER"] = Issuer,
        ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = Audience,
        ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
        ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
        ["FORTUNA_LOCALE"] = "pt-BR",
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = "false",
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10"
    };

    private sealed record MovementEnvelope(MovementData? Data);

    private sealed record MovementData(
        Guid Id,
        Guid InvestmentId,
        InvestmentMovementType MovementType,
        decimal Amount,
        string CurrencyCode,
        DateOnly OccurredOn,
        decimal Position,
        Guid? FinancialAccountId,
        decimal? FundingAmount,
        string? FundingCurrencyCode,
        Guid? TransferId,
        Guid? OutboundTransactionId,
        decimal? AppliedRate,
        DateOnly? RateDate,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
