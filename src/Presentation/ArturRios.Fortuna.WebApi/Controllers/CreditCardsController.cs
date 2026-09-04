using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.WebApi.Requests;
using ArturRios.Mediator.Command;
using ArturRios.Mediator.Query;
using ArturRios.Output;
using ArturRios.Util.WebApi.AspNetCore;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/credit-cards")]
public sealed class CreditCardsController(
    CommandMediator commandMediator,
    QueryMediator queryMediator) : Controller
{
    private static readonly HashSet<string> ListQueryFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "PageNumber",
        "PageSize",
        "Name",
        "Issuer",
        "CurrencyCode",
        "SortBy",
        "Descending"
    };

    private static readonly HashSet<string> StatementListQueryFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "PageNumber",
            "PageSize",
            "Status",
            "From",
            "To",
            "SortBy",
            "Descending"
        };

    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [CreditCardMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [CreditCardMessages.DuplicateName] = StatusCodes.Status409Conflict,
            [CreditCardMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [CreditCardMessages.NotFound] = StatusCodes.Status404NotFound,
            [CreditCardMessages.NameRequired] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.NameTooLong] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.IssuerRequired] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.IssuerTooLong] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.CurrencyRequired] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.CurrencyInvalid] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.CurrencyNotSupported] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.CurrencyImmutable] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.RestoreRequiresSoftDeletion] = StatusCodes.Status409Conflict,
            [CreditCardMessages.HardDeleteRequiresSoftDeletion] = StatusCodes.Status409Conflict,
            [CreditCardMessages.HardDeleteHasLiveTransactions] = StatusCodes.Status409Conflict,
            [CreditCardMessages.CreditLimitPositive] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.CreditLimitPrecisionInvalid] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.ClosingDayInvalid] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.DueDayInvalid] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.LastFourDigitsInvalid] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.InvalidPageNumber] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.InvalidPageSize] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.SortByUnsupported] = StatusCodes.Status400BadRequest,
            [CreditCardStatementMessages.CreditCardNotFound] = StatusCodes.Status404NotFound,
            [CreditCardStatementMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [CreditCardStatementMessages.InvalidPageNumber] = StatusCodes.Status400BadRequest,
            [CreditCardStatementMessages.InvalidPageSize] = StatusCodes.Status400BadRequest,
            [CreditCardStatementMessages.StatusInvalid] = StatusCodes.Status400BadRequest,
            [CreditCardStatementMessages.PeriodInvalid] = StatusCodes.Status400BadRequest,
            [CreditCardStatementMessages.SortByUnsupported] = StatusCodes.Status400BadRequest
        };

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CreateCreditCardCommandOutput?>>> Create(
        [FromBody] CreateCreditCardCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            CreateCreditCardCommand,
            CreateCreditCardCommandOutput>(command);

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPut("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<UpdateCreditCardCommandOutput?>>> Update(
        Guid id,
        [FromBody] UpdateCreditCardCommand command)
    {
        command.Id = id;
        var result = await commandMediator.ExecuteCommandAsync<
            UpdateCreditCardCommand,
            UpdateCreditCardCommandOutput>(command);

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpDelete("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CreditCardLifecycleCommandOutput?>>> Delete(Guid id)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            DeleteCreditCardCommand,
            CreditCardLifecycleCommandOutput>(new DeleteCreditCardCommand { Id = id });

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("{id:guid}/restore")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CreditCardLifecycleCommandOutput?>>> Restore(Guid id)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            RestoreCreditCardCommand,
            CreditCardLifecycleCommandOutput>(new RestoreCreditCardCommand { Id = id });

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpDelete("{id:guid}/hard")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CreditCardLifecycleCommandOutput?>>> HardDelete(Guid id)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            HardDeleteCreditCardCommand,
            CreditCardLifecycleCommandOutput>(new HardDeleteCreditCardCommand { Id = id });

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CreditCardOutput?>>> GetById(Guid id)
    {
        var result = await queryMediator.ExecuteQueryAsync<GetCreditCardByIdQuery, CreditCardOutput>(
            new GetCreditCardByIdQuery { Id = id });

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<PaginatedOutput<CreditCardOutput>>> List(
        [FromQuery] ListCreditCardsQuery query)
    {
        var unsupported = Request.Query.Keys.FirstOrDefault(key => !ListQueryFields.Contains(key));
        if (unsupported is not null)
        {
            return BadRequest(PaginatedOutput<CreditCardOutput>.New
                .WithError(CreditCardMessages.UnsupportedFilter(unsupported)));
        }

        var result = await queryMediator.ExecutePaginatedQueryAsync<
            ListCreditCardsQuery,
            CreditCardOutput>(query);

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet("{id:guid}/statements")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<PaginatedOutput<CreditCardStatementOutput>>> ListStatements(
        Guid id,
        [FromQuery] ListCreditCardStatementsRequest request)
    {
        var unsupported = Request.Query.Keys.FirstOrDefault(key =>
            !StatementListQueryFields.Contains(key));
        if (unsupported is not null)
        {
            return BadRequest(PaginatedOutput<CreditCardStatementOutput>.New
                .WithError(CreditCardStatementMessages.UnsupportedFilter(unsupported)));
        }

        var query = new ListCreditCardStatementsQuery
        {
            CreditCardId = id,
            Status = request.Status,
            From = request.From,
            To = request.To,
            SortBy = request.SortBy,
            Descending = request.Descending,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
        var result = await queryMediator.ExecutePaginatedQueryAsync<
            ListCreditCardStatementsQuery,
            CreditCardStatementOutput>(query);

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
