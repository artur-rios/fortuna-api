using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Command;
using ArturRios.Mediator.Query;
using ArturRios.Output;
using ArturRios.Util.WebApi.AspNetCore;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/accounts")]
public sealed class AccountsController(CommandMediator commandMediator, QueryMediator queryMediator) : Controller
{
    private static readonly HashSet<string> ListQueryFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "PageNumber",
        "PageSize",
        "Name",
        "Institution",
        "AccountType",
        "CurrencyCode",
        "IncludeDeleted",
        "SortBy",
        "Descending"
    };

    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [FinancialAccountMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [FinancialAccountMessages.DuplicateName] = StatusCodes.Status409Conflict,
            [FinancialAccountMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
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
            [FinancialAccountMessages.NotFound] = StatusCodes.Status404NotFound,
            [FinancialAccountMessages.InvalidPageNumber] = StatusCodes.Status400BadRequest,
            [FinancialAccountMessages.InvalidPageSize] = StatusCodes.Status400BadRequest,
            [FinancialAccountMessages.SortByUnsupported] = StatusCodes.Status400BadRequest
        };

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CreateFinancialAccountCommandOutput?>>> Create(
        [FromBody] CreateFinancialAccountCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            CreateFinancialAccountCommand,
            CreateFinancialAccountCommandOutput>(command);

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPut("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<UpdateFinancialAccountCommandOutput?>>> Update(
        Guid id,
        [FromBody] UpdateFinancialAccountCommand command)
    {
        command.Id = id;
        var result = await commandMediator.ExecuteCommandAsync<
            UpdateFinancialAccountCommand,
            UpdateFinancialAccountCommandOutput>(command);

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<FinancialAccountOutput?>>> GetById(
        Guid id,
        [FromQuery] bool includeDeleted = false)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            GetFinancialAccountByIdQuery,
            FinancialAccountOutput>(new GetFinancialAccountByIdQuery
            {
                Id = id,
                IncludeDeleted = includeDeleted
            });

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet("{id:guid}/balance")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<FinancialAccountBalanceOutput?>>> GetBalance(
        Guid id,
        [FromQuery] DateOnly? asOf = null)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            GetFinancialAccountBalanceQuery,
            FinancialAccountBalanceOutput>(new GetFinancialAccountBalanceQuery
            {
                Id = id,
                AsOf = asOf
            });

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpDelete("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<FinancialAccountLifecycleCommandOutput?>>> Delete(Guid id)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            DeleteFinancialAccountCommand,
            FinancialAccountLifecycleCommandOutput>(new DeleteFinancialAccountCommand { Id = id });

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("{id:guid}/restore")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<FinancialAccountLifecycleCommandOutput?>>> Restore(Guid id)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            RestoreFinancialAccountCommand,
            FinancialAccountLifecycleCommandOutput>(new RestoreFinancialAccountCommand { Id = id });

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpDelete("{id:guid}/hard")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<FinancialAccountLifecycleCommandOutput?>>> HardDelete(Guid id)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            HardDeleteFinancialAccountCommand,
            FinancialAccountLifecycleCommandOutput>(new HardDeleteFinancialAccountCommand { Id = id });

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<PaginatedOutput<FinancialAccountOutput>>> List(
        [FromQuery] ListFinancialAccountsQuery query)
    {
        var unsupported = Request.Query.Keys.FirstOrDefault(key => !ListQueryFields.Contains(key));
        if (unsupported is not null)
        {
            return BadRequest(PaginatedOutput<FinancialAccountOutput>.New
                .WithError(FinancialAccountMessages.UnsupportedFilter(unsupported)));
        }

        var result = await queryMediator.ExecutePaginatedQueryAsync<
            ListFinancialAccountsQuery,
            FinancialAccountOutput>(query);

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
