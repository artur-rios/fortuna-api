using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class ReassignCategoryTransactionsCommandHandler(
    ICurrentProfileResolver profileResolver,
    ICategoryTransactionReassigner categories,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<
        ReassignCategoryTransactionsCommand,
        ReassignCategoryTransactionsCommandOutput>
{
    public async Task<DataOutput<ReassignCategoryTransactionsCommandOutput?>> HandleAsync(
        ReassignCategoryTransactionsCommand command)
    {
        var output = DataOutput<ReassignCategoryTransactionsCommandOutput?>.New;
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return output.WithError(CategoryMessages.ProfileNotFound);
        }

        var result = await categories.ReassignAsync(
            new CategoryTransactionReassignment(
                profile.Id,
                command.Id,
                command.TargetCategoryId,
                command.IncludeDescendants,
                timeProvider.GetUtcNow()),
            CancellationToken.None);

        if (result.Outcome != CategoryTransactionReassignmentOutcome.Succeeded)
        {
            return result.Outcome switch
            {
                CategoryTransactionReassignmentOutcome.CategoryNotFound =>
                    output.WithError(CategoryMessages.NotFound),
                CategoryTransactionReassignmentOutcome.SameCategory =>
                    output.WithError(CategoryMessages.SourceAndTargetMustDiffer),
                _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, null)
            };
        }

        return output
            .WithData(new ReassignCategoryTransactionsCommandOutput
            {
                Id = command.Id,
                TargetCategoryId = command.TargetCategoryId,
                IncludeDescendants = command.IncludeDescendants,
                ReassignedCount = result.ReassignedCount
            })
            .WithMessage(CategoryMessages.TransactionsReassignedSuccessfully);
    }
}
