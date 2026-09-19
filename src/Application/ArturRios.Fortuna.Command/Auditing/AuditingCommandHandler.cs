using System.Reflection;
using ArturRios.Fortuna.Shared.Auditing;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Command;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using Microsoft.Extensions.Logging;

namespace ArturRios.Fortuna.Command.Auditing;

/// <summary>
///     Decorates a write handler with one best-effort audit entry: a success, a refusal, or — when
///     the inner handler throws — a refusal recording the unexpected failure before the exception
///     is rethrown.
/// </summary>
public sealed class AuditingCommandHandler<TCommand, TOutput>(
    ICommandHandlerAsync<TCommand, TOutput> inner,
    IAuditEntryWriter auditEntryWriter,
    ILogger<AuditingCommandHandler<TCommand, TOutput>> logger)
    : ICommandHandlerAsync<TCommand, TOutput>
    where TCommand : BaseCommand
    where TOutput : CommandOutput
{
    // Resolved once per closed generic type instead of on every request.
    private static readonly string Operation = typeof(TCommand).Name;
    private static readonly string EntityType = AuditEntityTypes.Resolve(Operation);
    private static readonly PropertyInfo? OutputIdProperty =
        GuidProperty(typeof(TOutput), "Id") ?? GuidProperty(typeof(TOutput), "PublicId");
    private static readonly PropertyInfo? CommandIdProperty = GuidProperty(typeof(TCommand), "Id");

    public async Task<DataOutput<TOutput?>> HandleAsync(TCommand command)
    {
        DataOutput<TOutput?> result;
        try
        {
            result = await inner.HandleAsync(command);
        }
        catch (Exception)
        {
            await WriteAsync(IdOf(CommandIdProperty, command), false, AuditEntryMessages.UnexpectedFailure);

            throw;
        }

        var entityPublicId = IdOf(OutputIdProperty, result.Data) ?? IdOf(CommandIdProperty, command);
        await WriteAsync(
            entityPublicId,
            result.Success,
            result.Success ? null : result.Errors.FirstOrDefault());

        return result;
    }

    private async Task WriteAsync(Guid? entityPublicId, bool succeeded, string? reason)
    {
        try
        {
            await auditEntryWriter.WriteAsync(
                Operation,
                entityPublicId.HasValue ? EntityType : null,
                entityPublicId,
                succeeded,
                reason);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to write audit entry for {Operation} (entity {EntityPublicId})",
                Operation,
                entityPublicId);
        }
    }

    private static Guid? IdOf(PropertyInfo? property, object? source)
    {
        if (property is null || source is null)
        {
            return null;
        }

        var value = (Guid?)property.GetValue(source);

        return value == Guid.Empty ? null : value;
    }

    private static PropertyInfo? GuidProperty(Type type, string name)
    {
        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

        return property is not null &&
               (property.PropertyType == typeof(Guid) || property.PropertyType == typeof(Guid?))
            ? property
            : null;
    }
}
