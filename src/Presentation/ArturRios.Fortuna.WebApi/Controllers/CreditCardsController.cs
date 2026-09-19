using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.WebApi.Requests;
using ArturRios.Output;
using ArturRios.Util.WebApi.Security.Attributes;
using ArturRios.Fortuna.WebApi.Filters;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/credit-cards")]
public sealed class CreditCardsController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [CreditCardMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [CreditCardMessages.DuplicateName] = StatusCodes.Status409Conflict,
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
            [CreditCardMessages.HardDeleteHasDependents] = StatusCodes.Status409Conflict,
            [CreditCardMessages.CreditLimitPositive] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.CreditLimitPrecisionInvalid] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.ClosingDayInvalid] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.DueDayInvalid] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.LastFourDigitsInvalid] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.InvalidPageNumber] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.InvalidPageSize] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.SortByUnsupported] = StatusCodes.Status400BadRequest,
            [CreditCardStatementMessages.CreditCardNotFound] = StatusCodes.Status404NotFound,
            [CreditCardStatementMessages.InvalidPageNumber] = StatusCodes.Status400BadRequest,
            [CreditCardStatementMessages.InvalidPageSize] = StatusCodes.Status400BadRequest,
            [CreditCardStatementMessages.StatusInvalid] = StatusCodes.Status400BadRequest,
            [CreditCardStatementMessages.PeriodInvalid] = StatusCodes.Status400BadRequest,
            [CreditCardStatementMessages.SortByUnsupported] = StatusCodes.Status400BadRequest
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CreateCreditCardCommandOutput?>>> Create(
        [FromBody] CreateCreditCardCommand command)
    {
        return await SendAsync<
            CreateCreditCardCommand,
            CreateCreditCardCommandOutput>(command);
    }

    [HttpPut("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<UpdateCreditCardCommandOutput?>>> Update(
        Guid id,
        [FromBody] UpdateCreditCardCommand command)
    {
        command.Id = id;

        return await SendAsync<
            UpdateCreditCardCommand,
            UpdateCreditCardCommandOutput>(command);
    }

    [HttpDelete("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CreditCardLifecycleCommandOutput?>>> Delete(Guid id)
    {
        return await SendAsync<
            DeleteCreditCardCommand,
            CreditCardLifecycleCommandOutput>(new DeleteCreditCardCommand { Id = id });
    }

    [HttpPost("{id:guid}/restore")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CreditCardLifecycleCommandOutput?>>> Restore(Guid id)
    {
        return await SendAsync<
            RestoreCreditCardCommand,
            CreditCardLifecycleCommandOutput>(new RestoreCreditCardCommand { Id = id });
    }

    [HttpDelete("{id:guid}/hard")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CreditCardLifecycleCommandOutput?>>> HardDelete(Guid id)
    {
        return await SendAsync<
            HardDeleteCreditCardCommand,
            CreditCardLifecycleCommandOutput>(new HardDeleteCreditCardCommand { Id = id });
    }

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CreditCardOutput?>>> GetById(Guid id)
    {
        return await QueryAsync<GetCreditCardByIdQuery, CreditCardOutput>(
            new GetCreditCardByIdQuery { Id = id });
    }

    [HttpGet]
    [AllowedQuery("PageNumber", "PageSize", "Name", "Issuer", "CurrencyCode", "IncludeDeleted", "SortBy", "Descending")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<PaginatedOutput<CreditCardOutput>>> List(
        [FromQuery] ListCreditCardsQuery query)
    {
        return await QueryPageAsync<
            ListCreditCardsQuery,
            CreditCardOutput>(query);
    }

    [HttpGet("{id:guid}/statements")]
    [AllowedQuery("PageNumber", "PageSize", "Status", "From", "To", "SortBy", "Descending")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<PaginatedOutput<CreditCardStatementOutput>>> ListStatements(
        Guid id,
        [FromQuery] ListCreditCardStatementsRequest request)
    {
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

        return await QueryPageAsync<
            ListCreditCardStatementsQuery,
            CreditCardStatementOutput>(query);
    }
}
