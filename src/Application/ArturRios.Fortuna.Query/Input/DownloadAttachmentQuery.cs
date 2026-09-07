using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class DownloadAttachmentQuery : BaseQuery
{
    public Guid Id { get; set; }
}
