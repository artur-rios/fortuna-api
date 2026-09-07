using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class DeleteCategoryCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ICategoryLifecycleStore categories,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<DeleteCategoryCommand, CategoryLifecycleCommandOutput>
{
    public async Task<DataOutput<CategoryLifecycleCommandOutput?>> HandleAsync(
        DeleteCategoryCommand command)
    {
        var profile = await CategoryLifecycleHandler.ResolveProfileAsync(
            actorAccessor.Actor,
            profiles);
        if (profile is null)
        {
            return CategoryLifecycleHandler.ProfileNotFound();
        }

        var result = await categories.SoftDeleteAsync(
            profile.Id,
            command.Id,
            timeProvider.GetUtcNow(),
            CancellationToken.None);
        return CategoryLifecycleHandler.Resolve(result, CategoryMessages.DeletedSuccessfully);
    }
}

public sealed class RestoreCategoryCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ICategoryLifecycleStore categories,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<RestoreCategoryCommand, CategoryLifecycleCommandOutput>
{
    public async Task<DataOutput<CategoryLifecycleCommandOutput?>> HandleAsync(
        RestoreCategoryCommand command)
    {
        var profile = await CategoryLifecycleHandler.ResolveProfileAsync(
            actorAccessor.Actor,
            profiles);
        if (profile is null)
        {
            return CategoryLifecycleHandler.ProfileNotFound();
        }

        var result = await categories.RestoreAsync(
            profile.Id,
            command.Id,
            timeProvider.GetUtcNow(),
            CancellationToken.None);
        return CategoryLifecycleHandler.Resolve(result, CategoryMessages.RestoredSuccessfully);
    }
}

public sealed class HardDeleteCategoryCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ICategoryLifecycleStore categories)
    : ICommandHandlerAsync<HardDeleteCategoryCommand, CategoryLifecycleCommandOutput>
{
    public async Task<DataOutput<CategoryLifecycleCommandOutput?>> HandleAsync(
        HardDeleteCategoryCommand command)
    {
        var profile = await CategoryLifecycleHandler.ResolveProfileAsync(
            actorAccessor.Actor,
            profiles);
        if (profile is null)
        {
            return CategoryLifecycleHandler.ProfileNotFound();
        }

        var result = await categories.HardDeleteAsync(
            profile.Id,
            command.Id,
            CancellationToken.None);
        return CategoryLifecycleHandler.Resolve(result, CategoryMessages.HardDeletedSuccessfully);
    }
}

internal static class CategoryLifecycleHandler
{
    public static async Task<UserProfileSnapshot?> ResolveProfileAsync(
        RequestActor? actor,
        IUserProfileReader profiles) => actor?.IsLocal == true
        ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
        : actor is null
            ? null
            : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    public static DataOutput<CategoryLifecycleCommandOutput?> ProfileNotFound() =>
        DataOutput<CategoryLifecycleCommandOutput?>.New
            .WithError(CategoryMessages.ProfileNotFound);

    public static DataOutput<CategoryLifecycleCommandOutput?> Resolve(
        CategoryLifecycleResult result,
        string successMessage)
    {
        var output = DataOutput<CategoryLifecycleCommandOutput?>.New;
        return result.Outcome switch
        {
            CategoryLifecycleOutcome.Succeeded => output
                .WithData(ToOutput(result))
                .WithMessage(successMessage),
            CategoryLifecycleOutcome.NotFound => output.WithError(CategoryMessages.NotFound),
            CategoryLifecycleOutcome.RestoreRequiresSoftDeletion => output
                .WithError(CategoryMessages.RestoreRequiresSoftDeletion),
            CategoryLifecycleOutcome.HardDeleteRequiresSoftDeletion => output
                .WithError(CategoryMessages.HardDeleteRequiresSoftDeletion),
            CategoryLifecycleOutcome.HardDeleteHasLiveTransactions => output
                .WithData(ToOutput(result))
                .WithError(CategoryMessages.HardDeleteHasLiveTransactions),
            CategoryLifecycleOutcome.DuplicateSiblingName => output
                .WithError(CategoryMessages.DuplicateSiblingName),
            CategoryLifecycleOutcome.AttachmentStorageUnavailable => output
                .WithError(AttachmentMessages.StorageUnavailable),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }

    private static CategoryLifecycleCommandOutput ToOutput(CategoryLifecycleResult result) => new()
    {
        Id = result.Id!.Value,
        LiveTransactionCount = result.LiveTransactionCount
    };
}
