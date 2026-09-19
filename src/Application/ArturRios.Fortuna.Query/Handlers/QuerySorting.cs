using System.Linq.Expressions;

namespace ArturRios.Fortuna.Query.Handlers;

/// <summary>
/// Orders list queries by a key in either direction, with a tie-breaker applied in the
/// same direction so pages stay stable.
/// </summary>
internal static class QuerySorting
{
    public static IOrderedQueryable<T> SortBy<T, TKey, TTieBreaker>(
        this IQueryable<T> source,
        Expression<Func<T, TKey>> key,
        Expression<Func<T, TTieBreaker>> tieBreaker,
        bool descending) => descending
        ? source.OrderByDescending(key).ThenByDescending(tieBreaker)
        : source.OrderBy(key).ThenBy(tieBreaker);

    public static IOrderedQueryable<T> ThenSortBy<T, TKey>(
        this IOrderedQueryable<T> source,
        Expression<Func<T, TKey>> key,
        bool descending) => descending
        ? source.ThenByDescending(key)
        : source.ThenBy(key);
}
