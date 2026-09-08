using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class GetDataExportQuery : BaseQuery
{
    public Guid Id { get; set; }
}
