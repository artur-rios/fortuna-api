using ArturRios.Fortuna.Command.Auditing;
using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Fortuna.Shared.Auditing;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests.Auditing;

public sealed class AuditEntryWriterTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-04T12:00:00Z");

    [UnitFact]
    public async Task GivenAuthenticatedActor_WhenSuccessIsWritten_ThenIdentityAndOutcomeArePreserved()
    {
        var actor = new RequestActor(Guid.NewGuid(), 3, Guid.NewGuid(), []);
        var store = new RecordingAuditEntryStore();
        var writer = new AuditEntryWriter(store, new StubActorAccessor(actor), new FixedTimeProvider(Now));
        var entityId = Guid.NewGuid();

        await writer.WriteAsync("RecordManualExchangeRateCommand", "ExchangeRate", entityId, true, "ignored");

        Assert.NotNull(store.Entry);
        Assert.Equal(actor.SubjectId, store.Entry.ActorSubjectId);
        Assert.Equal(AuditOutcome.Succeeded, store.Entry.Outcome);
        Assert.Null(store.Entry.Reason);
        Assert.Equal(entityId, store.Entry.EntityPublicId);
        Assert.Equal(Now, store.Entry.OccurredAt);
    }

    [UnitFact]
    public async Task GivenLongCanonicalReason_WhenRefusalIsWritten_ThenReasonIsSafelyBounded()
    {
        var store = new RecordingAuditEntryStore();
        var writer = new AuditEntryWriter(store, new StubActorAccessor(null), new FixedTimeProvider(Now));

        await writer.WriteAsync("WriteCommand", null, null, false, new string('x', 1001));

        Assert.Equal(AuditOutcome.Refused, store.Entry!.Outcome);
        Assert.Equal(1000, store.Entry.Reason!.Length);
        Assert.Null(store.Entry.ActorSubjectId);
    }

    private sealed class RecordingAuditEntryStore : IAuditEntryStore
    {
        public AuditEntryWrite? Entry { get; private set; }

        public Task AppendAsync(AuditEntryWrite entry, CancellationToken cancellationToken)
        {
            Entry = entry;
            return Task.CompletedTask;
        }
    }

    private sealed class StubActorAccessor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
