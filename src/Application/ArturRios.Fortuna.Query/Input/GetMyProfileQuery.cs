using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class GetMyProfileQuery : BaseQuery
{
    public Guid ExternalSubject { get; set; }
    public bool IsLocal { get; set; }
}
