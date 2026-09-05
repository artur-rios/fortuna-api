using System.Text.Json;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Shared.Tests;

public sealed class RecurringMaterializationJobHandlerTests
{
    [UnitFact]
    public async Task GivenScheduledPayload_WhenExecuted_ThenMaterializerReceivesRun()
    {
        var materializer = new StubMaterializer();
        var handler = new RecurringMaterializationJobHandler(materializer);
        var payload = new RecurringMaterializationJobPayload(
            Guid.NewGuid(),
            new DateOnly(2026, 9, 5),
            new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));

        await handler.ExecuteAsync(JsonSerializer.Serialize(payload), CancellationToken.None);

        Assert.Equal(payload.UserId, materializer.Run?.UserId);
        Assert.Equal(payload.Through, materializer.Run?.Through);
        Assert.Equal(payload.RequestedAt, materializer.Run?.MaterializedAt);
    }

    [UnitFact]
    public async Task GivenNullPayload_WhenExecuted_ThenItIsRejected()
    {
        var handler = new RecurringMaterializationJobHandler(new StubMaterializer());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.ExecuteAsync("null", CancellationToken.None));
    }

    private sealed class StubMaterializer : IRecurringTransactionMaterializer
    {
        public RecurringMaterializationRun? Run { get; private set; }

        public Task<RecurringMaterializationResult> MaterializeAsync(
            RecurringMaterializationRun run,
            CancellationToken cancellationToken)
        {
            Run = run;
            return Task.FromResult(new RecurringMaterializationResult([]));
        }
    }
}
