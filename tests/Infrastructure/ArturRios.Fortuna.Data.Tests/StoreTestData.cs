using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Attachments;

namespace ArturRios.Fortuna.Data.Tests;

internal sealed class StoreTestData
{
    public static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-10T12:00:00Z");
    public static readonly DateOnly Today = new(2026, 9, 10);

    private StoreTestData(Currency currency, UserProfile user, Category category)
    {
        Currency = currency;
        User = user;
        Category = category;
    }

    public Currency Currency { get; }
    public UserProfile User { get; }
    public Category Category { get; }

    public static async Task<StoreTestData> SeedAsync(AppDbContext context)
    {
        var currency = new Currency("BRL", "Brazilian Real", 2);
        var user = new UserProfile(Guid.NewGuid(), "Owner", currency, Now.AddDays(-30));
        var category = new Category(user, "General", Now.AddDays(-30));
        context.AddRange(currency, user, category);
        await context.SaveChangesAsync();

        return new StoreTestData(currency, user, category);
    }

    public FinancialAccount Account(AppDbContext context, string name, decimal openingBalance = 0m)
    {
        var account = new FinancialAccount(
            User,
            name,
            null,
            FinancialAccountType.Checking,
            Currency,
            openingBalance,
            Now.AddDays(-30));
        context.FinancialAccounts.Add(account);

        return account;
    }

    public FinancialTransaction Transaction(
        AppDbContext context,
        FinancialAccount account,
        TransactionDirection direction,
        decimal amount,
        DateOnly? occurredOn = null)
    {
        var transaction = new FinancialTransaction(
            User,
            account,
            Category,
            direction,
            amount,
            occurredOn ?? Today,
            Now);
        context.FinancialTransactions.Add(transaction);

        return transaction;
    }

    public Transfer Transfer(
        AppDbContext context,
        FinancialAccount source,
        FinancialAccount destination,
        decimal amount)
    {
        var transfer = new Transfer(
            Transaction(context, source, TransactionDirection.Expense, amount),
            Transaction(context, destination, TransactionDirection.Earning, amount),
            null,
            null,
            Now);
        context.Transfers.Add(transfer);

        return transfer;
    }
}

internal sealed class RecordingAttachmentStore(AppDbContext? observer = null) : IAttachmentStore
{
    private readonly Dictionary<string, byte[]> objects = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Keys => objects.Keys;
    public List<string> Deleted { get; } = [];
    public bool FailDeletes { get; init; }
    public List<bool> MetadataPresentAtDelete { get; } = [];

    public void Put(string key) => objects[key] = [1, 2, 3];

    public async Task WriteAsync(string key, Stream content, CancellationToken cancellationToken)
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        objects[key] = copy.ToArray();
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        if (!objects.TryGetValue(key, out var content))
        {
            throw new AttachmentObjectNotFoundException(key);
        }

        return Task.FromResult<Stream>(new MemoryStream(content, writable: false));
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        if (observer is not null)
        {
            MetadataPresentAtDelete.Add(observer.Attachments.Any(item => item.StorageKey == key));
        }

        if (FailDeletes)
        {
            throw new IOException("Injected object deletion failure.");
        }

        Deleted.Add(key);
        objects.Remove(key);

        return Task.CompletedTask;
    }

    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken) => Task.FromResult(true);
}
