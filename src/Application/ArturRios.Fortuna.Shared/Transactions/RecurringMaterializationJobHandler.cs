using System.Text.Json;
using ArturRios.Fortuna.Shared.Jobs;

namespace ArturRios.Fortuna.Shared.Transactions;

public sealed class RecurringMaterializationJobHandler(
    IRecurringTransactionMaterializer materializer) : IBackgroundJobHandler
{
    public string JobType => RecurringMaterializationJob.Type;

    public async Task ExecuteAsync(string payload, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<RecurringMaterializationJobPayload>(payload)
            ?? throw new InvalidOperationException(
                "The recurring transaction materialization payload is invalid.");
        await materializer.MaterializeAsync(
            new RecurringMaterializationRun(
                request.UserId,
                request.Through,
                request.RequestedAt),
            cancellationToken);
    }
}
