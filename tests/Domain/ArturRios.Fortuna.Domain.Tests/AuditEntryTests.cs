using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class AuditEntryTests
{
    [UnitFact]
    public void GivenCanonicalOperation_WhenEntryIsCreated_ThenAppendOnlyFieldsArePreserved()
    {
        var occurredAt = DateTimeOffset.Parse("2026-09-04T12:00:00Z");
        var entityId = Guid.NewGuid();

        var entry = new AuditEntry(
            null,
            "RecordManualExchangeRateCommand",
            "ExchangeRate",
            entityId,
            AuditOutcome.Refused,
            "Rate must be greater than zero.",
            occurredAt);

        Assert.Null(entry.UserId);
        Assert.Equal("RecordManualExchangeRateCommand", entry.Operation);
        Assert.Equal("ExchangeRate", entry.EntityType);
        Assert.Equal(entityId, entry.EntityPublicId);
        Assert.Equal(AuditOutcome.Refused, entry.Outcome);
        Assert.Equal(occurredAt, entry.OccurredAt);
    }

    [UnitFact]
    public void GivenOversizedReason_WhenEntryIsCreated_ThenInvariantRejectsIt()
    {
        Assert.Throws<ArgumentException>(() => new AuditEntry(
            null,
            "WriteCommand",
            null,
            null,
            AuditOutcome.Refused,
            new string('x', 1001),
            DateTimeOffset.UtcNow));
    }
}
