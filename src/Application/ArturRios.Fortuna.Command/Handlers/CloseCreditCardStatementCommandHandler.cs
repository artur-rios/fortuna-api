using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class CloseCreditCardStatementCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ICreditCardStatementCloser statements,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<CloseCreditCardStatementCommand, CloseCreditCardStatementCommandOutput>
{
    public async Task<DataOutput<CloseCreditCardStatementCommandOutput?>> HandleAsync(
        CloseCreditCardStatementCommand command)
    {
        var output = DataOutput<CloseCreditCardStatementCommandOutput?>.New;
        var actor = actorAccessor.Actor;
        var profile = actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
        if (profile is null)
        {
            return output.WithError(CreditCardStatementMessages.ProfileNotFound);
        }

        var now = timeProvider.GetUtcNow();
        var result = await statements.CloseAsync(
            profile.Id,
            command.Id,
            DateOnly.FromDateTime(now.UtcDateTime),
            explicitRequest: true,
            now,
            CancellationToken.None);
        if (result.Outcome == CreditCardStatementCloseOutcome.NotFound)
        {
            return output.WithError(CreditCardStatementMessages.NotFound);
        }

        if (result.Outcome == CreditCardStatementCloseOutcome.SettledStatementFrozen)
        {
            return output.WithError(CreditCardStatementMessages.SettledStatementFrozen);
        }

        var statement = result.Statement!;
        return output
            .WithData(new CloseCreditCardStatementCommandOutput
            {
                Id = statement.Id,
                CreditCardId = statement.CreditCardId,
                PeriodStart = statement.PeriodStart,
                PeriodEnd = statement.PeriodEnd,
                ClosingDate = statement.ClosingDate,
                DueDate = statement.DueDate,
                Status = statement.Status,
                PurchaseTotal = statement.PurchaseTotal,
                AmountDue = statement.AmountDue
            })
            .WithMessage(result.Outcome == CreditCardStatementCloseOutcome.NotDue
                ? CreditCardStatementMessages.NotDue
                : CreditCardStatementMessages.ClosedSuccessfully);
    }
}
