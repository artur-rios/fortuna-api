using ArturRios.Mediator.Command;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using Microsoft.Extensions.Logging;

namespace ArturRios.Fortuna.Command.Auditing;

/// <summary>Decorates a write handler with one best-effort audit entry.</summary>
public sealed class AuditingCommandHandler<TCommand, TOutput>(
    ICommandHandlerAsync<TCommand, TOutput> inner,
    IAuditEntryWriter auditEntryWriter,
    ILogger<AuditingCommandHandler<TCommand, TOutput>> logger)
    : ICommandHandlerAsync<TCommand, TOutput>
    where TCommand : BaseCommand
    where TOutput : CommandOutput
{
    public async Task<DataOutput<TOutput?>> HandleAsync(TCommand command)
    {
        var result = await inner.HandleAsync(command);
        var entityPublicId = ResolveEntityPublicId(result.Data);

        try
        {
            await auditEntryWriter.WriteAsync(
                typeof(TCommand).Name,
                entityPublicId.HasValue ? ResolveEntityType(typeof(TCommand).Name) : null,
                entityPublicId,
                result.Success,
                result.Success ? null : result.Errors.FirstOrDefault());
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to write audit entry for {Operation} (entity {EntityPublicId})",
                typeof(TCommand).Name,
                entityPublicId);
        }

        return result;
    }

    private static Guid? ResolveEntityPublicId(TOutput? output)
    {
        if (output is null)
        {
            return null;
        }

        var property = typeof(TOutput).GetProperty("Id") ?? typeof(TOutput).GetProperty("PublicId");

        return property is not null && property.PropertyType == typeof(Guid)
            ? (Guid?)property.GetValue(output)
            : null;
    }

    private static string ResolveEntityType(string operation) => operation
        .Replace("Command", string.Empty, StringComparison.Ordinal)
        .Replace("HardDelete", string.Empty, StringComparison.Ordinal)
        .Replace("Create", string.Empty, StringComparison.Ordinal)
        .Replace("Record", string.Empty, StringComparison.Ordinal)
        .Replace("Update", string.Empty, StringComparison.Ordinal)
        .Replace("Delete", string.Empty, StringComparison.Ordinal)
        .Replace("Restore", string.Empty, StringComparison.Ordinal)
        .Replace("Regenerate", string.Empty, StringComparison.Ordinal);
}
