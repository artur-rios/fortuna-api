namespace ArturRios.Fortuna.Shared.Pagination;

public sealed record PageRequest(int PageNumber, int PageSize)
{
    public int Skip => (PageNumber - 1) * PageSize;
}

public sealed record ReadPage<T>(IReadOnlyCollection<T> Items, int TotalItems);
