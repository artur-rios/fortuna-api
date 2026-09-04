using ArturRios.Fortuna.Domain.Lifecycle;

namespace ArturRios.Fortuna.Data.Lifecycle;

public static class RecordLifecycleQueryExtensions
{
    public static IQueryable<TEntity> WhereLive<TEntity>(this IQueryable<TEntity> source)
        where TEntity : RecordLifecycleEntity => source.Where(entity => !entity.IsDeleted);
}
