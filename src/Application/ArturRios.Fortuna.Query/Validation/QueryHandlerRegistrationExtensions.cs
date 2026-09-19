using ArturRios.Mediator.Query;
using ArturRios.Mediator.Query.Interfaces;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace ArturRios.Fortuna.Query.Validation;

public static class QueryHandlerRegistrationExtensions
{
    /// <summary>Registers a read handler behind its validator.</summary>
    public static IServiceCollection AddValidatedQueryHandler<TQuery, TOutput, THandler, TValidator>(
        this IServiceCollection services)
        where TQuery : BaseQuery
        where TOutput : QueryOutput
        where THandler : class, IQueryHandlerAsync<TQuery, TOutput>
        where TValidator : class, IValidator<TQuery>
    {
        services.AddScoped<IValidator<TQuery>, TValidator>();
        services.AddScoped<THandler>();
        services.AddScoped<IQueryHandlerAsync<TQuery, TOutput>>(provider =>
            new ValidatingQueryHandler<TQuery, TOutput>(
                provider.GetRequiredService<THandler>(),
                provider.GetRequiredService<IValidator<TQuery>>()));

        return services;
    }

    /// <summary>Registers a paginated read handler behind its validator.</summary>
    public static IServiceCollection AddValidatedPaginatedQueryHandler<TQuery, TOutput, THandler, TValidator>(
        this IServiceCollection services)
        where TQuery : BaseQuery
        where TOutput : QueryOutput
        where THandler : class, IPaginatedQueryHandlerAsync<TQuery, TOutput>
        where TValidator : class, IValidator<TQuery>
    {
        services.AddScoped<IValidator<TQuery>, TValidator>();
        services.AddScoped<THandler>();
        services.AddScoped<IPaginatedQueryHandlerAsync<TQuery, TOutput>>(provider =>
            new ValidatingPaginatedQueryHandler<TQuery, TOutput>(
                provider.GetRequiredService<THandler>(),
                provider.GetRequiredService<IValidator<TQuery>>()));

        return services;
    }
}
