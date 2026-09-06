using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Jobs;
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
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class PdfInvoiceImportTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private static readonly DateTimeOffset Now =
        new(2027, 2, 2, 14, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("fortuna")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    [FunctionalFact]
    public async Task GivenNubankInvoice_WhenProcessedTwice_ThenStatementAndDuplicatesAreReported()
    {
        var subject = Guid.NewGuid();
        var cardId = await SeedCardAsync(subject, withPreviousStatement: true);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var pdf = InvoicePdf();

        var first = await ImportAsync(client, cardId, pdf);
        var firstJobId = (await first.Content.ReadFromJsonAsync<ImportEnvelope>())!.Data!.ImportJobId;
        await ProcessNextAsync(factory);
        var second = await ImportAsync(client, cardId, pdf);
        var secondJobId = (await second.Content.ReadFromJsonAsync<ImportEnvelope>())!.Data!.ImportJobId;
        await ProcessNextAsync(factory);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        await using var context = CreateContext();
        var firstJob = await context.ImportJobs.SingleAsync(item => item.PublicId == firstJobId);
        var secondJob = await context.ImportJobs.SingleAsync(item => item.PublicId == secondJobId);
        Assert.Equal(ImportJobStatus.Completed, firstJob.Status);
        Assert.Equal((8, 0, 0),
            (firstJob.ImportedCount, firstJob.DuplicateCount, firstJob.RejectedCount));
        Assert.Equal((0, 8, 0),
            (secondJob.ImportedCount, secondJob.DuplicateCount, secondJob.RejectedCount));
        Assert.Equal(new DateOnly(2026, 12, 14), firstJob.PeriodStart);
        Assert.Equal(new DateOnly(2027, 1, 12), firstJob.PeriodEnd);

        var statement = await context.CreditCardStatements.SingleAsync(item =>
            item.CreditCard.PublicId == cardId && item.PeriodEnd == new DateOnly(2027, 1, 12));
        Assert.Equal(CreditCardStatementStatus.Closed, statement.Status);
        Assert.Equal((100m, 100m, 165m, 5m, -10m, 160m),
            (statement.PreviousBalance, statement.PaymentsReceived, statement.PurchaseTotal,
                statement.ForeignTaxTotal, statement.OtherEntries, statement.AmountDue));
        Assert.Equal(7, await context.FinancialTransactions.CountAsync(item =>
            item.StatementId == statement.Id));
        Assert.Equal(8, await context.ImportedRecords.CountAsync(item =>
            item.ImportJob.PublicId == firstJobId));

        var foreign = await context.FinancialTransactions
            .Include(item => item.OriginalCurrency)
            .SingleAsync(item => item.ImportedRecord!.ImportJob.PublicId == firstJobId &&
                item.OriginalAmount != null);
        Assert.Equal(4m, foreign.OriginalAmount);
        Assert.Equal("USD", foreign.OriginalCurrency!.Code);
        Assert.Equal(5m, foreign.AppliedRate);
        var installment = await context.FinancialTransactions.SingleAsync(item =>
            item.ImportedRecord!.ImportJob.PublicId == firstJobId &&
            item.InstallmentNumber != null);
        Assert.Equal((short)2, installment.InstallmentNumber);
        Assert.NotNull(installment.InstallmentPlanId);
        var plan = await context.InstallmentPlans
            .Include(item => item.Installments)
            .SingleAsync(item => item.Id == installment.InstallmentPlanId);
        Assert.Equal(2, plan.Installments.Count);

        var taxRecord = await context.ImportedRecords.SingleAsync(item =>
            item.ImportJob.PublicId == firstJobId &&
            EF.Functions.JsonContains(item.RawPayload, "{\"kind\":\"Tax\"}"));
        Assert.Contains("relatedLineSequence", taxRecord.RawPayload, StringComparison.Ordinal);
        var credit = await context.FinancialTransactions.SingleAsync(item =>
            item.ImportedRecord!.ImportJob.PublicId == firstJobId &&
            item.Description == "Credito de confianca");
        Assert.Equal(TransactionDirection.Earning, credit.Direction);
        Assert.Null(credit.CounterpartyId);

        var previous = await context.CreditCardStatements.SingleAsync(item =>
            item.CreditCard.PublicId == cardId && item.PeriodEnd == new DateOnly(2026, 12, 13));
        Assert.Equal(CreditCardStatementStatus.Settled, previous.Status);
        Assert.NotNull(previous.SettlementTransactionId);
    }

    [FunctionalFact]
    public async Task GivenSettledInvoicePeriod_WhenProcessed_ThenLinesMoveToNextOpenStatement()
    {
        var subject = Guid.NewGuid();
        var cardId = await SeedSettledCurrentStatementAsync(subject);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var response = await ImportAsync(client, cardId, InvoicePdf());
        await ProcessNextAsync(factory);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await using var context = CreateContext();
        var settled = await context.CreditCardStatements.SingleAsync(item =>
            item.CreditCard.PublicId == cardId && item.PeriodEnd == new DateOnly(2027, 1, 12));
        Assert.Equal(CreditCardStatementStatus.Settled, settled.Status);
        Assert.Equal(160m, settled.AmountDue);
        Assert.Equal(0, await context.FinancialTransactions.CountAsync(item =>
            item.StatementId == settled.Id && item.SourceType == TransactionSourceType.Pdf));
        var next = await context.CreditCardStatements.SingleAsync(item =>
            item.CreditCard.PublicId == cardId && item.PeriodStart > settled.PeriodEnd &&
            item.Status == CreditCardStatementStatus.Open);
        var late = await context.FinancialTransactions
            .Where(item => item.StatementId == next.Id)
            .ToArrayAsync();
        Assert.Equal(7, late.Length);
        Assert.All(late, transaction => Assert.True(transaction.IsLateArriving));
    }

    [FunctionalFact]
    public async Task GivenUnknownOrTextlessPdf_WhenProcessed_ThenJobFailsWithoutRecords()
    {
        var subject = Guid.NewGuid();
        var cardId = await SeedCardAsync(subject);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var unknownResponse = await ImportAsync(client, cardId, TextPdf(["Another bank", "STATEMENT"]));
        var unknownJobId = (await unknownResponse.Content.ReadFromJsonAsync<ImportEnvelope>())!
            .Data!.ImportJobId;
        await ProcessNextAsync(factory);
        var textlessResponse = await ImportAsync(client, cardId, TextlessPdf());
        var textlessJobId = (await textlessResponse.Content.ReadFromJsonAsync<ImportEnvelope>())!
            .Data!.ImportJobId;
        await ProcessNextAsync(factory);

        await using var context = CreateContext();
        var unknown = await context.ImportJobs.SingleAsync(item => item.PublicId == unknownJobId);
        var textless = await context.ImportJobs.SingleAsync(item => item.PublicId == textlessJobId);
        Assert.Equal(ImportJobStatus.Failed, unknown.Status);
        Assert.Equal(PdfInvoiceImportMessages.UnsupportedLayout, unknown.FailureReason);
        Assert.Equal(ImportJobStatus.Failed, textless.Status);
        Assert.Equal(PdfInvoiceImportMessages.NoTextLayer, textless.FailureReason);
        Assert.False(await context.ImportedRecords.AnyAsync(item =>
            item.ImportJobId == unknown.Id || item.ImportJobId == textless.Id));
    }

    [FunctionalFact]
    public async Task GivenOversizedPdf_WhenUploaded_ThenNothingIsQueued()
    {
        var subject = Guid.NewGuid();
        var cardId = await SeedCardAsync(subject);
        await using var factory = CreateFactory(maximumBytes: 10);
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var response = await ImportAsync(client, cardId, new byte[11]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(PdfInvoiceImportMessages.FileTooLarge,
            await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.False(await context.ImportJobs.AnyAsync(item =>
            item.User.ExternalSubject == subject.ToString("D")));
    }

    [FunctionalFact]
    public async Task GivenForeignOrMissingCard_WhenUploaded_ThenResponsesAreIndistinguishable()
    {
        var ownerSubject = Guid.NewGuid();
        var otherSubject = Guid.NewGuid();
        var cardId = await SeedCardAsync(ownerSubject);
        await SeedCardAsync(otherSubject);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, otherSubject, HeimdallRoles.User);

        var foreign = await ImportAsync(client, cardId, InvoicePdf());
        var missing = await ImportAsync(client, Guid.NewGuid(), InvoicePdf());

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(
            (await missing.Content.ReadFromJsonAsync<ErrorEnvelope>())?.Errors,
            (await foreign.Content.ReadFromJsonAsync<ErrorEnvelope>())?.Errors);
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private WebApplicationFactory<Program> CreateFactory(int maximumBytes = 20 * 1024 * 1024)
    {
        foreach (var setting in ValidSettings(maximumBytes))
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
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
                services.AddDbContext<AppDbContext>(options =>
                    options.UseNpgsql(database.GetConnectionString()));
            });
        });
    }

    private async Task<Guid> SeedCardAsync(Guid subject, bool withPreviousStatement = false)
    {
        await using var context = CreateContext();
        var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var user = new UserProfile(subject, $"Owner {subject:N}", currency, Now);
        var card = new CreditCard(
            user, $"Card {subject:N}", "Nubank", currency, 5000m, 12, 10, "1234", Now);
        context.AddRange(user, card);
        if (withPreviousStatement)
        {
            var category = new Category(user, "General", Now);
            var statement = new CreditCardStatement(card, new BillingCycle(
                new DateOnly(2026, 11, 14),
                new DateOnly(2026, 12, 13),
                new DateOnly(2026, 12, 13),
                new DateOnly(2027, 1, 10)), Now);
            statement.ApplyImportedSummary(0m, 0m, 100m, 0m, 0m, 100m, Now);
            statement.Close(Now);
            var plan = new InstallmentPlan(
                card, 300m, 6, new DateOnly(2026, 11, 28), Now);
            var firstInstallment = new FinancialTransaction(
                user, card, category, TransactionDirection.Expense, 50m,
                new DateOnly(2026, 11, 28), Now, "Curso exemplo");
            plan.AddInstallment(firstInstallment, 1, Now);
            context.AddRange(category, statement, plan, firstInstallment);
        }

        await context.SaveChangesAsync();
        return card.PublicId;
    }

    private async Task<Guid> SeedSettledCurrentStatementAsync(Guid subject)
    {
        await using var context = CreateContext();
        var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var user = new UserProfile(subject, $"Owner {subject:N}", currency, Now);
        var card = new CreditCard(
            user, $"Card {subject:N}", "Nubank", currency, 5000m, 12, 10, "1234", Now);
        var category = new Category(user, "General", Now);
        var statement = new CreditCardStatement(card, new BillingCycle(
            new DateOnly(2026, 12, 14),
            new DateOnly(2027, 1, 12),
            new DateOnly(2027, 1, 12),
            new DateOnly(2027, 2, 10)), Now);
        statement.ApplyImportedSummary(100m, 100m, 165m, 5m, -10m, 160m, Now);
        statement.Close(Now);
        var settlement = new FinancialTransaction(
            user, card, category, TransactionDirection.Earning, 160m,
            new DateOnly(2027, 2, 10), Now, "Statement payment");
        statement.Settle(settlement, Now);
        context.AddRange(user, card, category, statement, settlement);
        await context.SaveChangesAsync();
        return card.PublicId;
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

    private static async Task ProcessNextAsync(WebApplicationFactory<Program> factory)
    {
        var queue = factory.Services.GetRequiredService<IBackgroundJobQueue>();
        var id = await queue.DequeueAsync(CancellationToken.None);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BackgroundJobProcessor>()
            .ProcessAsync(id, CancellationToken.None);
    }

    private static async Task<HttpResponseMessage> ImportAsync(
        HttpClient client,
        Guid cardId,
        byte[] pdf)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(cardId.ToString("D")), "CreditCardId");
        form.Add(new ByteArrayContent(pdf), "File", "invoice.pdf");
        return await client.PostAsync("/api/imports/pdf", form);
    }

    private static byte[] InvoicePdf() => TextPdf(
    [
        "Nubank",
        "FATURA 10 FEV 2027 EMISSAO E ENVIO 01 FEV 2027",
        "TRANSACOES",
        "Fatura anterior R$ 100,00",
        "Pagamento recebido -R$ 100,00",
        "Total de compras de todos os cartoes, 14 DEZ a 12 JAN R$ 165,00",
        "IOF de compras internacionais R$ 5,00",
        "Outros lancamentos -R$ 10,00",
        "Subtotal do cartao R$ 170,00",
        "Total a pagar R$ 160,00",
        "20 DEZ 1234 Mercado exemplo R$ 100,00",
        "28 DEZ Curso exemplo Parcela 2/6 R$ 50,00",
        "03 JAN Loja exterior R$ 20,00",
        "BRL 20,00 = USD 4,00",
        "Conversao: BRL 5,00 = USD 1 = R$ 5,00",
        "03 JAN IOF de \"Loja exterior\" R$ 6,00",
        "04 JAN IOF de volta de \"Loja exterior\" -R$ 1,00",
        "04 JAN Estorno de \"Mercado exemplo\" -R$ 5,00",
        "05 JAN Credito de confianca -R$ 10,00",
        "Pagamentos -R$ 100,00",
        "Pagamento em 05 JAN -R$ 100,00"
    ]);

    private static byte[] TextPdf(IEnumerable<string> lines)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        var y = 800;
        foreach (var line in lines)
        {
            page.AddText(line, 9, new PdfPoint(25, y), font);
            y -= 24;
        }

        return builder.Build();
    }

    private static byte[] TextlessPdf()
    {
        using var builder = new PdfDocumentBuilder();
        _ = builder.AddPage(PageSize.A4);
        return builder.Build();
    }

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Account Owner"
        };
        var configuration = new JwtConfiguration(
            3600, Issuer, Audience, Secret, new FortunaIdentityMapper().ToClaims(identity));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtHandler().CreateToken(configuration));
    }

    private static Dictionary<string, string?> ValidSettings(int maximumBytes) => new()
    {
        ["FORTUNA_DATA_CONNECTIONSTRING"] =
            "Host=localhost;Database=fortuna;Username=postgres;Password=postgres;Search Path=fortuna",
        ["FORTUNA_DATA_DATABASETYPE"] = "PostgreSql",
        ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
        ["FORTUNA_STORAGE_PATH"] = Path.Combine(Path.GetTempPath(), "fortuna-api-tests"),
        ["FORTUNA_LOG_DIRECTORY"] = Path.Combine(Path.GetTempPath(), "fortuna-api-test-logs"),
        ["FORTUNA_JOB_QUEUE_CAPACITY"] = "32",
        ["FORTUNA_EXCEL_IMPORT_MAX_BYTES"] = (10 * 1024 * 1024).ToString(
            CultureInfo.InvariantCulture),
        ["FORTUNA_PDF_IMPORT_MAX_BYTES"] = maximumBytes.ToString(CultureInfo.InvariantCulture),
        ["FORTUNA_AUTH_TOKEN_SECRET"] = Secret,
        ["FORTUNA_AUTH_TOKEN_ISSUER"] = Issuer,
        ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = Audience,
        ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
        ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
        ["FORTUNA_LOCALE"] = "pt-BR",
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = "false",
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10"
    };

    private sealed record ImportEnvelope(ImportData? Data);
    private sealed record ImportData(Guid ImportJobId, ImportJobStatus Status);
    private sealed record ErrorEnvelope(IReadOnlyList<string> Errors);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
