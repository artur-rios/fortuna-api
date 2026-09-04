namespace ArturRios.Fortuna.Command.Auditing;

public interface IAuditEntryWriter
{
    Task WriteAsync(
        string operation,
        string? entityType,
        Guid? entityPublicId,
        bool succeeded,
        string? reason);
}
