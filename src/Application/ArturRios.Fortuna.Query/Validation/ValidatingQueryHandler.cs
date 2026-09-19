using ArturRios.Mediator.Query;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Validation;

/// <summary>
///     Decorates a read handler with its FluentValidation validator: an invalid query is refused
///     with every validation message, in the validator's order, before the inner handler runs.
/// </summary>
public sealed class ValidatingQueryHandler<TQuery, TOutput>(
    IQueryHandlerAsync<TQuery, TOutput> inner,
    IValidator<TQuery> validator)
    : IQueryHandlerAsync<TQuery, TOutput>
    where TQuery : BaseQuery
    where TOutput : QueryOutput
{
    public async Task<DataOutput<TOutput?>> HandleAsync(TQuery query)
    {
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return DataOutput<TOutput?>.New.WithErrors(
                validation.Errors.Select(failure => failure.ErrorMessage));
        }

        return await inner.HandleAsync(query);
    }
}

/// <summary>The paginated counterpart of <see cref="ValidatingQueryHandler{TQuery,TOutput}" />.</summary>
public sealed class ValidatingPaginatedQueryHandler<TQuery, TOutput>(
    IPaginatedQueryHandlerAsync<TQuery, TOutput> inner,
    IValidator<TQuery> validator)
    : IPaginatedQueryHandlerAsync<TQuery, TOutput>
    where TQuery : BaseQuery
    where TOutput : QueryOutput
{
    public async Task<PaginatedOutput<TOutput>> HandleAsync(TQuery query)
    {
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return PaginatedOutput<TOutput>.New.WithErrors(
                validation.Errors.Select(failure => failure.ErrorMessage));
        }

        return await inner.HandleAsync(query);
    }
}
