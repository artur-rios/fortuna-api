using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Shared.Classification;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArturRios.Fortuna.Data.Classification;

public sealed class EfTagStore(AppDbContext context, TagOptions options)
    : ITagStore, ITagReader, ITagUpdater, ITagLifecycleStore, ITransactionTagStore
{
    private const string LiveNameIndex = "ix_tag_user_id_normalized_name";

    public async Task<TagCreationResult> CreateAsync(
        TagCreation creation,
        CancellationToken cancellationToken)
    {
        var user = await context.UserProfiles.SingleAsync(
            item => item.PublicId == creation.UserId,
            cancellationToken);
        var normalizedName = creation.Name.Trim().ToUpperInvariant();
        if (await context.Tags.AnyAsync(item =>
            item.UserId == user.Id &&
            item.NormalizedName == normalizedName &&
            !item.IsDeleted,
            cancellationToken))
        {
            return CreationResult(TagMutationOutcome.DuplicateName);
        }

        var tag = new Tag(user, creation.Name, creation.CreatedAt);
        context.Tags.Add(tag);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateName(exception))
        {
            context.Entry(tag).State = EntityState.Detached;
            return CreationResult(TagMutationOutcome.DuplicateName);
        }

        return new TagCreationResult(Snapshot(tag), TagMutationOutcome.Succeeded);
    }

    public async Task<IReadOnlyCollection<TagSnapshot>> ListAsync(
        Guid userId,
        bool includeDeleted,
        CancellationToken cancellationToken) => await context.Tags
        .AsNoTracking()
        .Where(item =>
            item.User.PublicId == userId &&
            (includeDeleted || !item.IsDeleted))
        .OrderBy(item => item.Name)
        .ThenBy(item => item.PublicId)
        .Select(item => new TagSnapshot(
            item.PublicId,
            item.Name,
            item.IsDeleted,
            item.CreatedAt,
            item.UpdatedAt))
        .ToArrayAsync(cancellationToken);

    public async Task<TagUpdateResult> UpdateAsync(
        TagUpdate update,
        CancellationToken cancellationToken)
    {
        var tag = await FindTrackedAsync(update.UserId, update.Id, cancellationToken);
        if (tag is null || tag.IsDeleted)
        {
            return UpdateResult(TagMutationOutcome.NotFound);
        }

        var normalizedName = update.Name.Trim().ToUpperInvariant();
        if (await context.Tags.AnyAsync(item =>
            item.Id != tag.Id &&
            item.UserId == tag.UserId &&
            item.NormalizedName == normalizedName &&
            !item.IsDeleted,
            cancellationToken))
        {
            return UpdateResult(TagMutationOutcome.DuplicateName);
        }

        tag.Rename(update.Name, update.UpdatedAt);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateName(exception))
        {
            context.Entry(tag).State = EntityState.Detached;
            return UpdateResult(TagMutationOutcome.DuplicateName);
        }

        return new TagUpdateResult(Snapshot(tag), TagMutationOutcome.Succeeded);
    }

    public async Task<TagDeletionResult> SoftDeleteAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var tag = await FindTrackedAsync(userId, id, cancellationToken);
        if (tag is null)
        {
            return DeletionResult(TagMutationOutcome.NotFound);
        }

        var transactions = await context.FinancialTransactions
            .Include(item => item.Tags)
            .Where(item => item.Tags.Any(itemTag => itemTag.Id == tag.Id))
            .ToListAsync(cancellationToken);
        foreach (var transaction in transactions)
        {
            transaction.DetachTag(tag, changedAt);
        }

        tag.SoftDelete(changedAt);
        await context.SaveChangesAsync(cancellationToken);
        return new TagDeletionResult(
            Snapshot(tag),
            transactions.Count,
            TagMutationOutcome.Succeeded);
    }

    public async Task<TransactionTagAssignmentResult> AttachAsync(
        TransactionTagAssignment assignment,
        CancellationToken cancellationToken)
    {
        var entities = await FindAssignmentAsync(assignment, cancellationToken);
        if (entities.Transaction is null || entities.Tag is null)
        {
            return AssignmentResult(TransactionTagAssignmentOutcome.NotFound);
        }

        if (entities.Transaction.Tags.Any(item => item.Id == entities.Tag.Id))
        {
            return AssignmentResult(
                TransactionTagAssignmentOutcome.Succeeded,
                entities.Transaction.PublicId,
                entities.Tag.PublicId,
                isAttached: true,
                changed: false,
                entities.Transaction.Tags.Count);
        }

        if (entities.Transaction.Tags.Count >= options.MaximumPerTransaction)
        {
            return AssignmentResult(
                TransactionTagAssignmentOutcome.MaximumExceeded,
                entities.Transaction.PublicId,
                entities.Tag.PublicId,
                isAttached: false,
                changed: false,
                entities.Transaction.Tags.Count);
        }

        var changed = entities.Transaction.AttachTag(entities.Tag, assignment.ChangedAt);
        await context.SaveChangesAsync(cancellationToken);
        return AssignmentResult(
            TransactionTagAssignmentOutcome.Succeeded,
            entities.Transaction.PublicId,
            entities.Tag.PublicId,
            isAttached: true,
            changed,
            entities.Transaction.Tags.Count);
    }

    public async Task<TransactionTagAssignmentResult> DetachAsync(
        TransactionTagAssignment assignment,
        CancellationToken cancellationToken)
    {
        var entities = await FindAssignmentAsync(assignment, cancellationToken);
        if (entities.Transaction is null || entities.Tag is null)
        {
            return AssignmentResult(TransactionTagAssignmentOutcome.NotFound);
        }

        var changed = entities.Transaction.DetachTag(entities.Tag, assignment.ChangedAt);
        if (changed)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return AssignmentResult(
            TransactionTagAssignmentOutcome.Succeeded,
            entities.Transaction.PublicId,
            entities.Tag.PublicId,
            isAttached: false,
            changed,
            entities.Transaction.Tags.Count);
    }

    private async Task<(Domain.Transactions.FinancialTransaction? Transaction, Tag? Tag)>
        FindAssignmentAsync(
            TransactionTagAssignment assignment,
            CancellationToken cancellationToken)
    {
        var transaction = await context.FinancialTransactions
            .Include(item => item.User)
            .Include(item => item.Tags)
                .ThenInclude(item => item.User)
            .SingleOrDefaultAsync(item =>
                item.User.PublicId == assignment.UserId &&
                item.PublicId == assignment.TransactionId &&
                !item.IsDeleted,
                cancellationToken);
        var tag = await context.Tags
            .Include(item => item.User)
            .SingleOrDefaultAsync(item =>
                item.User.PublicId == assignment.UserId &&
                item.PublicId == assignment.TagId &&
                !item.IsDeleted,
                cancellationToken);
        return (transaction, tag);
    }

    private Task<Tag?> FindTrackedAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken) => context.Tags
        .SingleOrDefaultAsync(item =>
            item.User.PublicId == userId && item.PublicId == id,
            cancellationToken);

    private static bool IsDuplicateName(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: LiveNameIndex
        };

    private static TagSnapshot Snapshot(Tag tag) => new(
        tag.PublicId,
        tag.Name,
        tag.IsDeleted,
        tag.CreatedAt,
        tag.UpdatedAt);

    private static TagCreationResult CreationResult(TagMutationOutcome outcome) =>
        new(null, outcome);

    private static TagUpdateResult UpdateResult(TagMutationOutcome outcome) =>
        new(null, outcome);

    private static TagDeletionResult DeletionResult(TagMutationOutcome outcome) =>
        new(null, 0, outcome);

    private static TransactionTagAssignmentResult AssignmentResult(
        TransactionTagAssignmentOutcome outcome,
        Guid? transactionId = null,
        Guid? tagId = null,
        bool isAttached = false,
        bool changed = false,
        int tagCount = 0) => new(
            transactionId,
            tagId,
            isAttached,
            changed,
            tagCount,
            outcome);
}
