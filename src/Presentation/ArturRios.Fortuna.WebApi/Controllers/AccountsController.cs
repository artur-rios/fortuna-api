using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using ArturRios.Util.WebApi.Security.Attributes;
using ArturRios.Fortuna.WebApi.Filters;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/accounts")]
public sealed class AccountsController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [FinancialAccountMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [FinancialAccountMessages.DuplicateName] = StatusCodes.Status409Conflict,
            [FinancialAccountMessages.NameRequired] = StatusCodes.Status400BadRequest,
            [FinancialAccountMessages.NameTooLong] = StatusCodes.Status400BadRequest,
            [FinancialAccountMessages.InstitutionTooLong] = StatusCodes.Status400BadRequest,
            [FinancialAccountMessages.AccountTypeInvalid] = StatusCodes.Status400BadRequest,
            [FinancialAccountMessages.CurrencyRequired] = StatusCodes.Status400BadRequest,
            [FinancialAccountMessages.CurrencyInvalid] = StatusCodes.Status400BadRequest,
            [FinancialAccountMessages.CurrencyNotSupported] = StatusCodes.Status400BadRequest,
            [FinancialAccountMessages.OpeningBalancePrecisionInvalid] = StatusCodes.Status400BadRequest,
            [FinancialAccountMessages.OwnerImmutable] = StatusCodes.Status400BadRequest,
            [FinancialAccountMessages.CurrencyImmutable] = StatusCodes.Status400BadRequest,
            [FinancialAccountMessages.OpeningBalanceImmutable] = StatusCodes.Status400BadRequest,
            [FinancialAccountMessages.RestoreRequiresSoftDeletion] = StatusCodes.Status409Conflict,
            [FinancialAccountMessages.HardDeleteRequiresSoftDeletion] = StatusCodes.Status409Conflict,
            [FinancialAccountMessages.HardDeleteHasLiveTransactions] = StatusCodes.Status409Conflict,
            [FinancialAccountMessages.HardDeleteHasDependents] = StatusCodes.Status409Conflict,
            [FinancialAccountMessages.NotFound] = StatusCodes.Status404NotFound,
            [FinancialAccountMessages.InvalidPageNumber] = StatusCodes.Status400BadRequest,
            [FinancialAccountMessages.AsOfOutOfRange] = StatusCodes.Status400BadRequest,
            [FinancialAccountMessages.InvalidPageSize] = StatusCodes.Status400BadRequest,
            [FinancialAccountMessages.SortByUnsupported] = StatusCodes.Status400BadRequest
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CreateFinancialAccountCommandOutput?>>> Create(
        [FromBody] CreateFinancialAccountCommand command)
    {
        return await SendAsync<
            CreateFinancialAccountCommand,
            CreateFinancialAccountCommandOutput>(command);
    }

    [HttpPut("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<UpdateFinancialAccountCommandOutput?>>> Update(
        Guid id,
        [FromBody] UpdateFinancialAccountCommand command)
    {
        command.Id = id;

        return await SendAsync<
            UpdateFinancialAccountCommand,
            UpdateFinancialAccountCommandOutput>(command);
    }

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<FinancialAccountOutput?>>> GetById(
        Guid id,
        [FromQuery] bool includeDeleted = false)
    {
        return await QueryAsync<
            GetFinancialAccountByIdQuery,
            FinancialAccountOutput>(new GetFinancialAccountByIdQuery
            {
                Id = id,
                IncludeDeleted = includeDeleted
            });
    }

    [HttpGet("{id:guid}/balance")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<FinancialAccountBalanceOutput?>>> GetBalance(
        Guid id,
        [FromQuery] DateOnly? asOf = null)
    {
        return await QueryAsync<
            GetFinancialAccountBalanceQuery,
            FinancialAccountBalanceOutput>(new GetFinancialAccountBalanceQuery
            {
                Id = id,
                AsOf = asOf
            });
    }

    [HttpDelete("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<FinancialAccountLifecycleCommandOutput?>>> Delete(Guid id)
    {
        return await SendAsync<
            DeleteFinancialAccountCommand,
            FinancialAccountLifecycleCommandOutput>(new DeleteFinancialAccountCommand { Id = id });
    }

    [HttpPost("{id:guid}/restore")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<FinancialAccountLifecycleCommandOutput?>>> Restore(Guid id)
    {
        return await SendAsync<
            RestoreFinancialAccountCommand,
            FinancialAccountLifecycleCommandOutput>(new RestoreFinancialAccountCommand { Id = id });
    }

    [HttpDelete("{id:guid}/hard")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<FinancialAccountLifecycleCommandOutput?>>> HardDelete(Guid id)
    {
        return await SendAsync<
            HardDeleteFinancialAccountCommand,
            FinancialAccountLifecycleCommandOutput>(new HardDeleteFinancialAccountCommand { Id = id });
    }

    [HttpGet]
    [AllowedQuery(
        "PageNumber", "PageSize", "Name", "Institution", "AccountType", "CurrencyCode", "IncludeDeleted", "SortBy",
        "Descending")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<PaginatedOutput<FinancialAccountOutput>>> List(
        [FromQuery] ListFinancialAccountsQuery query)
    {
        return await QueryPageAsync<
            ListFinancialAccountsQuery,
            FinancialAccountOutput>(query);
    }
}
