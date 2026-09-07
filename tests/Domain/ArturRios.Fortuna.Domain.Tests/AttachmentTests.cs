using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Attachments;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class AttachmentTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 23, 30, 0, TimeSpan.Zero);

    [UnitFact]
    public void GivenValidMetadata_WhenAttachmentCreated_ThenMetadataIsNormalizedAndFixed()
    {
        var transaction = Transaction();

        var attachment = new Attachment(
            transaction,
            " receipt.pdf ",
            " application/pdf ",
            42,
            " attachments/key ",
            Now);

        Assert.Equal(transaction, attachment.Transaction);
        Assert.Equal("receipt.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal(42, attachment.SizeInBytes);
        Assert.Equal("attachments/key", attachment.StorageKey);
        Assert.Equal(Now, attachment.CreatedAt);
        Assert.False(attachment.IsDeleted);
    }

    [UnitTheory]
    [InlineData("fileName")]
    [InlineData("contentType")]
    [InlineData("storageKey")]
    public void GivenMissingRequiredMetadata_WhenAttachmentCreated_ThenItIsRejected(string field)
    {
        Assert.Throws<ArgumentException>(() => new Attachment(
            Transaction(),
            field == "fileName" ? " " : "receipt.pdf",
            field == "contentType" ? " " : "application/pdf",
            42,
            field == "storageKey" ? " " : "attachments/key",
            Now));
    }

    [UnitFact]
    public void GivenNonPositiveSize_WhenAttachmentCreated_ThenItIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Attachment(
            Transaction(),
            "receipt.pdf",
            "application/pdf",
            0,
            "attachments/key",
            Now));
    }

    private static FinancialTransaction Transaction()
    {
        var currency = new Currency("BRL", "Brazilian real", 2);
        var user = new UserProfile(Guid.NewGuid(), "Owner", currency, Now);
        var account = new FinancialAccount(
            user,
            "Daily",
            null,
            FinancialAccountType.Checking,
            currency,
            0m,
            Now);
        return new FinancialTransaction(
            user,
            account,
            new Category(user, "General", Now),
            TransactionDirection.Expense,
            42m,
            new DateOnly(2026, 9, 6),
            Now);
    }
}
