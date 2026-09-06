using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Ingestion;

public sealed class EfPdfInvoiceImportStore(AppDbContext context) : IPdfInvoiceImportStore
{
    public async Task<QueuePdfInvoiceImportResult> QueueAsync(
        PdfInvoiceImportRequest request,
        CancellationToken cancellationToken)
    {
        var user = await context.UserProfiles.SingleAsync(
            item => item.PublicId == request.UserId,
            cancellationToken);
        var card = await context.CreditCards.SingleOrDefaultAsync(item =>
            item.UserId == user.Id && item.PublicId == request.CreditCardId,
            cancellationToken);
        if (card is null)
        {
            return Result(QueuePdfInvoiceImportOutcome.CreditCardNotFound);
        }

        if (card.IsDeleted)
        {
            return Result(QueuePdfInvoiceImportOutcome.CreditCardDeleted);
        }

        var importJob = new ImportJob(user, TransactionSourceType.Pdf, request.CreatedAt);
        var payload = JsonSerializer.Serialize(new PdfInvoiceImportJobPayload(
            importJob.PublicId,
            request.UserId,
            request.CreditCardId,
            request.Content));
        var backgroundJob = BackgroundJob.Create(
            PdfInvoiceImportJob.Type,
            payload,
            $"{PdfInvoiceImportJob.Type}:{importJob.PublicId:N}",
            request.CorrelationId,
            request.CreatedAt);
        context.ImportJobs.Add(importJob);
        context.BackgroundJobs.Add(backgroundJob);
        await context.SaveChangesAsync(cancellationToken);
        return new QueuePdfInvoiceImportResult(
            Snapshot(importJob),
            backgroundJob.Id,
            QueuePdfInvoiceImportOutcome.Succeeded);
    }

    public async Task<bool> BeginAsync(
        Guid importJobId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        var job = await context.ImportJobs.SingleOrDefaultAsync(item =>
            item.PublicId == importJobId && item.SourceType == TransactionSourceType.Pdf,
            cancellationToken);
        if (job is null || job.Status is ImportJobStatus.Completed or ImportJobStatus.Failed)
        {
            return false;
        }

        if (job.Status == ImportJobStatus.Pending)
        {
            job.Start(startedAt);
            await context.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task CompleteAsync(
        Guid importJobId,
        Guid userId,
        Guid creditCardId,
        ParsedPdfInvoice invoice,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        await using var databaseTransaction = await context.Database.BeginTransactionAsync(
            cancellationToken);
        try
        {
            var job = await context.ImportJobs.Include(item => item.User).SingleAsync(item =>
                item.PublicId == importJobId && item.User.PublicId == userId &&
                item.SourceType == TransactionSourceType.Pdf,
                cancellationToken);
            if (job.Status != ImportJobStatus.Running)
            {
                return;
            }

            var card = await context.CreditCards
                .Include(item => item.Currency)
                .SingleOrDefaultAsync(item =>
                    item.UserId == job.UserId && item.PublicId == creditCardId && !item.IsDeleted,
                    cancellationToken) ?? throw new InvalidOperationException(
                        "The PDF invoice target is unavailable.");
            var category = await UncategorizedAsync(job, completedAt, cancellationToken);
            var statements = await context.CreditCardStatements
                .Where(item => item.CreditCardId == card.Id && !item.IsDeleted)
                .OrderBy(item => item.PeriodStart)
                .ToListAsync(cancellationToken);
            var intendedStatement = statements.SingleOrDefault(item =>
                item.PeriodStart == invoice.PeriodStart && item.PeriodEnd == invoice.PeriodEnd);
            if (intendedStatement is null)
            {
                intendedStatement = new CreditCardStatement(card, new BillingCycle(
                    invoice.PeriodStart,
                    invoice.PeriodEnd,
                    invoice.PeriodEnd,
                    invoice.DueDate), completedAt);
                context.CreditCardStatements.Add(intendedStatement);
                statements.Add(intendedStatement);
            }

            var isLateArriving = intendedStatement.Status == CreditCardStatementStatus.Settled;
            var targetStatement = isLateArriving
                ? NextOpenStatement(card, intendedStatement, statements, completedAt)
                : intendedStatement;
            if (context.Entry(targetStatement).State == EntityState.Detached)
            {
                context.CreditCardStatements.Add(targetStatement);
            }
            if (!isLateArriving)
            {
                intendedStatement.ApplyImportedSummary(
                    invoice.PreviousBalance,
                    invoice.PaymentsReceived,
                    invoice.PurchaseTotal,
                    invoice.ForeignTaxTotal,
                    invoice.OtherEntries,
                    invoice.AmountDue,
                    completedAt);
            }

            var imported = 0;
            var duplicates = 0;
            var signatures = new HashSet<string>(StringComparer.Ordinal);
            var paymentTransactions = new List<FinancialTransaction>();
            foreach (var line in invoice.Lines.OrderBy(item => item.Sequence))
            {
                var amount = Math.Abs(line.SignedAmount);
                if (amount == 0m)
                {
                    throw new InvalidOperationException("A PDF invoice line cannot have a zero amount.");
                }

                var externalId = ExternalId(invoice, line);
                var duplicate = !signatures.Add(externalId) || await IsDuplicateAsync(
                    card.Id,
                    externalId,
                    cancellationToken);
                if (duplicate)
                {
                    context.ImportedRecords.Add(new ImportedRecord(
                        job,
                        line.RawPayload,
                        ImportedRecordOutcome.Duplicate,
                        amount,
                        line.OccurredOn,
                        externalId));
                    duplicates++;
                    continue;
                }

                var record = new ImportedRecord(
                    job,
                    line.RawPayload,
                    ImportedRecordOutcome.Imported,
                    amount,
                    line.OccurredOn,
                    externalId);
                var transaction = new FinancialTransaction(
                    job.User,
                    card,
                    category,
                    line.SignedAmount < 0m
                        ? TransactionDirection.Earning
                        : TransactionDirection.Expense,
                    amount,
                    line.OccurredOn,
                    completedAt,
                    Description(line.Description));
                transaction.MarkAsImported(record, TransactionSourceType.Pdf, completedAt);
                if (line.OriginalAmount.HasValue && line.AppliedRate.HasValue &&
                    !string.IsNullOrWhiteSpace(line.OriginalCurrencyCode))
                {
                    var originalCurrency = await context.Currencies.SingleOrDefaultAsync(item =>
                        item.Code == line.OriginalCurrencyCode,
                        cancellationToken) ?? throw new InvalidOperationException(
                            $"Currency '{line.OriginalCurrencyCode}' is not supported.");
                    transaction.RecordForeignCurrencyDetails(
                        line.OriginalAmount.Value,
                        originalCurrency,
                        line.AppliedRate.Value,
                        line.OccurredOn,
                        completedAt);
                }

                if (line.Kind == PdfInvoiceLineKind.Payment)
                {
                    paymentTransactions.Add(transaction);
                }
                else
                {
                    transaction.AssignToStatement(targetStatement, isLateArriving, completedAt);
                    if (line.InstallmentNumber.HasValue && line.InstallmentCount.HasValue &&
                        transaction.Direction == TransactionDirection.Expense)
                    {
                        await AssignInstallmentAsync(
                            transaction,
                            line,
                            card,
                            completedAt,
                            cancellationToken);
                    }
                }

                context.ImportedRecords.Add(record);
                context.FinancialTransactions.Add(transaction);
                imported++;
            }

            if (!isLateArriving)
            {
                intendedStatement.Close(completedAt);
            }

            SettlePreviousStatement(
                statements,
                intendedStatement,
                invoice.PaymentsReceived,
                paymentTransactions,
                completedAt);
            job.SetPeriod(invoice.PeriodStart, invoice.PeriodEnd, completedAt);
            job.Complete(imported, duplicates, 0, completedAt);
            await context.SaveChangesAsync(cancellationToken);
            await databaseTransaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await databaseTransaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task FailAsync(
        Guid importJobId,
        string reason,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken)
    {
        var job = await context.ImportJobs.SingleAsync(item =>
            item.PublicId == importJobId && item.SourceType == TransactionSourceType.Pdf,
            cancellationToken);
        if (job.Status is ImportJobStatus.Pending or ImportJobStatus.Running)
        {
            job.Fail(reason, failedAt);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<Category> UncategorizedAsync(
        ImportJob job,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        const string normalizedName = "UNCATEGORIZED";
        var category = await context.Categories.SingleOrDefaultAsync(item =>
            item.UserId == job.UserId && item.ParentId == null &&
            item.NormalizedName == normalizedName && !item.IsDeleted,
            cancellationToken);
        if (category is not null)
        {
            return category;
        }

        category = new Category(job.User, "Uncategorized", createdAt);
        context.Categories.Add(category);
        return category;
    }

    private static CreditCardStatement NextOpenStatement(
        CreditCard card,
        CreditCardStatement settled,
        ICollection<CreditCardStatement> statements,
        DateTimeOffset changedAt)
    {
        var open = statements
            .Where(item => item.PeriodStart > settled.PeriodEnd &&
                item.Status == CreditCardStatementStatus.Open)
            .OrderBy(item => item.PeriodStart)
            .FirstOrDefault();
        if (open is not null)
        {
            return open;
        }

        var cycle = BillingCycle.Containing(
            settled.PeriodEnd.AddDays(1),
            card.ClosingDay,
            card.DueDay);
        while (statements.Any(item => item.PeriodStart == cycle.PeriodStart &&
            item.PeriodEnd == cycle.PeriodEnd))
        {
            cycle = cycle.Next(card.ClosingDay, card.DueDay);
        }

        open = new CreditCardStatement(card, cycle, changedAt);
        statements.Add(open);
        return open;
    }

    private async Task AssignInstallmentAsync(
        FinancialTransaction transaction,
        ParsedPdfInvoiceLine line,
        CreditCard card,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var plans = await context.InstallmentPlans
            .Include(plan => plan.Installments)
            .Where(plan => plan.CreditCardId == card.Id && !plan.IsDeleted &&
                plan.InstallmentCount == line.InstallmentCount)
            .ToListAsync(cancellationToken);
        var normalizedDescription = NormalizeDescription(line.Description);
        var plan = plans
            .Where(candidate => candidate.Installments.All(installment =>
                installment.InstallmentNumber != line.InstallmentNumber))
            .OrderBy(candidate => Math.Abs(candidate.PurchasedOn.DayNumber -
                line.OccurredOn.AddMonths(-(line.InstallmentNumber!.Value - 1)).DayNumber))
            .FirstOrDefault(candidate => candidate.Installments.Any(installment =>
                NormalizeDescription(installment.Description) == normalizedDescription));
        if (plan is null)
        {
            plan = new InstallmentPlan(
                card,
                transaction.Amount * line.InstallmentCount!.Value,
                line.InstallmentCount.Value,
                line.OccurredOn.AddMonths(-(line.InstallmentNumber!.Value - 1)),
                changedAt);
            context.InstallmentPlans.Add(plan);
        }

        plan.AddInstallment(transaction, line.InstallmentNumber!.Value, changedAt);
    }

    private static void SettlePreviousStatement(
        IEnumerable<CreditCardStatement> statements,
        CreditCardStatement current,
        decimal paymentsReceived,
        IReadOnlyList<FinancialTransaction> payments,
        DateTimeOffset changedAt)
    {
        if (payments.Count == 0 || paymentsReceived <= 0m)
        {
            return;
        }

        var previous = statements
            .Where(item => item.PeriodEnd < current.PeriodStart &&
                item.Status == CreditCardStatementStatus.Closed)
            .OrderByDescending(item => item.PeriodEnd)
            .FirstOrDefault();
        if (previous is not null &&
            Math.Abs(payments.Sum(item => item.Amount) - paymentsReceived) <= 0.01m &&
            Math.Abs(previous.AmountDue - paymentsReceived) <= 0.01m)
        {
            previous.Settle(payments[^1], changedAt);
        }
    }

    private Task<bool> IsDuplicateAsync(
        long cardId,
        string externalId,
        CancellationToken cancellationToken) => context.FinancialTransactions
        .AsNoTracking()
        .AnyAsync(item => !item.IsDeleted && item.CreditCardId == cardId &&
            item.ImportedRecord != null && item.ImportedRecord.ExternalId == externalId,
            cancellationToken);

    private static string ExternalId(ParsedPdfInvoice invoice, ParsedPdfInvoiceLine line)
    {
        var source = string.Join('|',
            invoice.PeriodStart.ToString("O"),
            invoice.PeriodEnd.ToString("O"),
            line.Sequence.ToString("D4"),
            line.OccurredOn.ToString("O"),
            line.MaskedCardNumber,
            line.Description.Trim().ToUpperInvariant(),
            line.SignedAmount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            line.Kind);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        return $"nubank:{invoice.PeriodEnd:yyyyMMdd}:{line.Sequence:D4}:{hash[..32]}";
    }

    private static string NormalizeDescription(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string? Description(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : value.Trim()[..Math.Min(value.Trim().Length, 500)];

    private static QueuePdfInvoiceImportResult Result(QueuePdfInvoiceImportOutcome outcome) =>
        new(null, null, outcome);

    private static PdfInvoiceImportJobSnapshot Snapshot(ImportJob job) => new(
        job.PublicId,
        job.Status,
        job.CreatedAt,
        job.UpdatedAt);
}
