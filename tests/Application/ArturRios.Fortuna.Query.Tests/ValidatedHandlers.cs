using ArturRios.Fortuna.Query.Validation;
using ArturRios.Mediator.Query;
using ArturRios.Mediator.Query.Interfaces;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Tests;

/// <summary>Puts a handler behind its validator, the way the application registers it.</summary>
internal static class ValidatedHandlers
{
    public static IQueryHandlerAsync<TQuery, TOutput> Validated<TQuery, TOutput>(
        this IQueryHandlerAsync<TQuery, TOutput> handler,
        IValidator<TQuery> validator)
        where TQuery : BaseQuery
        where TOutput : QueryOutput => new ValidatingQueryHandler<TQuery, TOutput>(handler, validator);

    public static IPaginatedQueryHandlerAsync<TQuery, TOutput> Validated<TQuery, TOutput>(
        this IPaginatedQueryHandlerAsync<TQuery, TOutput> handler,
        IValidator<TQuery> validator)
        where TQuery : BaseQuery
        where TOutput : QueryOutput => new ValidatingPaginatedQueryHandler<TQuery, TOutput>(handler, validator);
}
