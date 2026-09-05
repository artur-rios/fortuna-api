using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetInstallmentPlanByIdQueryHandler(
    IValidator<GetInstallmentPlanByIdQuery> validator,
    IUserProfileReader profiles,
    IInstallmentPlanReader plans,
    IRequestActorAccessor actorAccessor)
    : IQueryHandlerAsync<GetInstallmentPlanByIdQuery, InstallmentPlanOutput>
{
    public async Task<DataOutput<InstallmentPlanOutput?>> HandleAsync(
        GetInstallmentPlanByIdQuery query)
    {
        var output = DataOutput<InstallmentPlanOutput?>.New;
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var actor = actorAccessor.Actor;
        var profile = actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
        if (profile is null)
        {
            return output.WithError(InstallmentPlanMessages.ProfileNotFound);
        }

        var plan = await plans.FindByIdAsync(
            profile.Id,
            query.Id,
            query.IncludeDeleted,
            CancellationToken.None);
        if (plan is null)
        {
            return output.WithError(InstallmentPlanMessages.NotFound);
        }

        return output.WithData(new InstallmentPlanOutput
        {
            Id = plan.Id,
            CreditCardId = plan.CreditCardId,
            TotalAmount = plan.TotalAmount,
            CurrencyCode = plan.CurrencyCode,
            OriginalTotalAmount = plan.OriginalTotalAmount,
            OriginalCurrencyCode = plan.OriginalCurrencyCode,
            AppliedRate = plan.AppliedRate,
            RateDate = plan.RateDate,
            InstallmentCount = plan.InstallmentCount,
            PurchasedOn = plan.PurchasedOn,
            IsDeleted = plan.IsDeleted,
            CreatedAt = plan.CreatedAt,
            UpdatedAt = plan.UpdatedAt,
            Installments = plan.Installments.Select(item => new InstallmentOutput
            {
                TransactionId = item.TransactionId,
                Number = item.Number,
                Amount = item.Amount,
                CurrencyCode = item.CurrencyCode,
                OriginalAmount = item.OriginalAmount,
                OriginalCurrencyCode = item.OriginalCurrencyCode,
                AppliedRate = item.AppliedRate,
                RateDate = item.RateDate,
                OccurredOn = item.OccurredOn,
                StatementId = item.StatementId,
                IsLateArriving = item.IsLateArriving,
                IsDeleted = item.IsDeleted
            }).ToArray()
        }).WithMessage(InstallmentPlanMessages.RetrievedSuccessfully);
    }
}
