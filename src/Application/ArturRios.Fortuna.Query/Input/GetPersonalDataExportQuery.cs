using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class GetPersonalDataExportQuery : BaseQuery
{
    public Guid JobId { get; set; }
}
