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
[Route("api/transactions")]
public sealed class TransactionsController(
    CommandMediator commandMediator,
    QueryMediator queryMediator) : Controller
{
    private static readonly HashSet<string> SearchQueryFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "PageNumber",
        "PageSize",
        "From",
        "To",
        "FinancialAccountId",
        "CreditCardId",
        "CategoryId",
        "TagId",
        "CounterpartyId",
        "Direction",
        "MinimumAmount",
        "MaximumAmount",
        "Text",
        "IncludeDeleted",
        "DisplayCurrencyCode",
        "FigureDate",
        "SortBy",
        "Descending"
    };

    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [TransactionMessages.RecordedSuccessfully] = StatusCodes.Status201Created,
            [TransactionMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [TransactionMessages.FinancialAccountNotFound] = StatusCodes.Status404NotFound,
            [TransactionMessages.CreditCardNotFound] = StatusCodes.Status404NotFound,
            [TransactionMessages.CategoryNotFound] = StatusCodes.Status404NotFound,
            [TransactionMessages.CurrencyNotSupported] = StatusCodes.Status400BadRequest,
            [TransactionMessages.ExchangeRateUnavailable] = StatusCodes.Status409Conflict,
            [TransactionMessages.ConvertedAmountTooSmall] = StatusCodes.Status400BadRequest,
            [TransactionMessages.AmountPositive] = StatusCodes.Status400BadRequest,
            [TransactionMessages.AmountPrecisionInvalid] = StatusCodes.Status400BadRequest,
            [TransactionMessages.OccurredOnRequired] = StatusCodes.Status400BadRequest,
            [TransactionMessages.OccurredOnTooFarInFuture] = StatusCodes.Status400BadRequest,
            [TransactionMessages.DirectionInvalid] = StatusCodes.Status400BadRequest,
            [TransactionMessages.ExactlyOneTargetRequired] = StatusCodes.Status400BadRequest,
            [TransactionMessages.CategoryIdRequired] = StatusCodes.Status400BadRequest,
            [TransactionMessages.CurrencyInvalid] = StatusCodes.Status400BadRequest,
            [TransactionMessages.DescriptionTooLong] = StatusCodes.Status400BadRequest,
            [TransactionMessages.CounterpartyTooLong] = StatusCodes.Status400BadRequest,
            [TransactionMessages.TooManyTags] = StatusCodes.Status400BadRequest,
            [TransactionMessages.TagRequired] = StatusCodes.Status400BadRequest,
            [TransactionMessages.TagTooLong] = StatusCodes.Status400BadRequest,
            [TransactionMessages.OwnerImmutable] = StatusCodes.Status400BadRequest,
            [TransactionMessages.NotFound] = StatusCodes.Status404NotFound,
            [TransactionMessages.TransactionIdRequired] = StatusCodes.Status400BadRequest,
            [TransactionMessages.InvalidPageNumber] = StatusCodes.Status400BadRequest,
            [TransactionMessages.InvalidPageSize] = StatusCodes.Status400BadRequest,
            [TransactionMessages.DateRangeInvalid] = StatusCodes.Status400BadRequest,
            [TransactionMessages.FinancialAccountIdInvalid] = StatusCodes.Status400BadRequest,
            [TransactionMessages.CreditCardIdInvalid] = StatusCodes.Status400BadRequest,
            [TransactionMessages.CategoryFilterIdInvalid] = StatusCodes.Status400BadRequest,
            [TransactionMessages.TagIdInvalid] = StatusCodes.Status400BadRequest,
            [TransactionMessages.CounterpartyIdInvalid] = StatusCodes.Status400BadRequest,
            [TransactionMessages.MinimumAmountInvalid] = StatusCodes.Status400BadRequest,
            [TransactionMessages.MaximumAmountInvalid] = StatusCodes.Status400BadRequest,
            [TransactionMessages.AmountRangeInvalid] = StatusCodes.Status400BadRequest,
            [TransactionMessages.SearchTextTooLong] = StatusCodes.Status400BadRequest,
            [TransactionMessages.DisplayCurrencyInvalid] = StatusCodes.Status400BadRequest,
            [TransactionMessages.SortByUnsupported] = StatusCodes.Status400BadRequest
        };

    [HttpGet]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<TransactionSearchOutput?>>> Search(
        [FromQuery] SearchTransactionsQuery query)
    {
        var unsupported = Request.Query.Keys.FirstOrDefault(key =>
            !SearchQueryFields.Contains(key));
        if (unsupported is not null)
        {
            return BadRequest(DataOutput<TransactionSearchOutput?>.New
                .WithError(TransactionMessages.UnsupportedFilter(unsupported)));
        }

        var result = await queryMediator.ExecuteQueryAsync<
            SearchTransactionsQuery,
            TransactionSearchOutput>(query);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<TransactionOutput?>>> GetById(
        Guid id,
        [FromQuery] bool includeDeleted = false)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            GetTransactionByIdQuery,
            TransactionOutput>(new GetTransactionByIdQuery
            {
                Id = id,
                IncludeDeleted = includeDeleted
            });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<RecordTransactionCommandOutput?>>> Record(
        [FromBody] RecordTransactionCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            RecordTransactionCommand,
            RecordTransactionCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
