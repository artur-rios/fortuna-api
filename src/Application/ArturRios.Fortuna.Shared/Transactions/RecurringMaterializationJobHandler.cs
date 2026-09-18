using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;

namespace ArturRios.Fortuna.Shared.Transactions;

public sealed class RecurringMaterializationJobHandler(
    IRecurringTransactionMaterializer materializer) : IBackgroundJobHandler
{
    public string JobType => RecurringMaterializationJob.Type;

    public async Task<ProcessOutput> ExecuteAsync(string payload, CancellationToken cancellationToken)
    {
        if (!JobPayload.TryRead<RecurringMaterializationJobPayload>(payload, out var request))
        {
            return ProcessOutput.New.WithError(BackgroundJobMessages.PayloadInvalid);
        }

        await materializer.MaterializeAsync(
            new RecurringMaterializationRun(
                request.UserId,
                request.Through,
                request.RequestedAt),
            cancellationToken);

        return ProcessOutput.New;
    }
}
