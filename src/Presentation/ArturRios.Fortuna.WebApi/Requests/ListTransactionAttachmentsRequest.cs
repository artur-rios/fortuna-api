namespace ArturRios.Fortuna.WebApi.Requests;

public sealed class ListTransactionAttachmentsRequest
{
    public bool IncludeDeleted { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}
