using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Query;
using ArturRios.Output;
using ArturRios.Util.WebApi.AspNetCore;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/reports")]
public sealed class ReportsController(QueryMediator queryMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [TableReportMessages.RetrievedSuccessfully] = StatusCodes.Status200OK,
            [TableReportMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [TableReportMessages.RecordSetRequired] = StatusCodes.Status400BadRequest,
            [TableReportMessages.ColumnsRequired] = StatusCodes.Status400BadRequest,
            [TableReportMessages.ColumnsMustBeUnique] = StatusCodes.Status400BadRequest,
            [TableReportMessages.InvalidPageNumber] = StatusCodes.Status400BadRequest,
            [TableReportMessages.InvalidPageSize] = StatusCodes.Status400BadRequest,
            [TableReportMessages.FilterFieldRequired] = StatusCodes.Status400BadRequest,
            [TableReportMessages.FilterOperatorRequired] = StatusCodes.Status400BadRequest,
            [TableReportMessages.FilterValueRequired] = StatusCodes.Status400BadRequest,
            [TableReportMessages.SortFieldRequired] = StatusCodes.Status400BadRequest,
            [TableReportMessages.DisplayCurrencyInvalid] = StatusCodes.Status400BadRequest,
            [TableReportMessages.DisplayCurrencyUnsupported] = StatusCodes.Status400BadRequest
        };

    [HttpPost("table")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<TableReportOutput?>>> QueryTable(
        [FromBody] QueryRecordsAsTableQuery query)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            QueryRecordsAsTableQuery,
            TableReportOutput>(query);
        if (result.Errors?.Count > 0 &&
            !result.Errors.Contains(TableReportMessages.ProfileNotFound))
        {
            return BadRequest(result);
        }

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet("aggregate")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<TransactionAggregationOutput?>>> Aggregate(
        [FromQuery] AggregateTransactionsQuery query)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            AggregateTransactionsQuery,
            TransactionAggregationOutput>(query);
        if (result.Errors?.Count > 0 &&
            !result.Errors.Contains(TransactionAggregationMessages.ProfileNotFound))
        {
            return BadRequest(result);
        }

        return ResponseResolver.Resolve(result, statusMap: new Dictionary<string, int>
        {
            [TransactionAggregationMessages.RetrievedSuccessfully] = StatusCodes.Status200OK,
            [TransactionAggregationMessages.ProfileNotFound] = StatusCodes.Status404NotFound
        });
    }

    [HttpGet("drill-down")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<TransactionDrillDownOutput?>>> DrillDown(
        [FromQuery] DrillIntoAggregationQuery query)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            DrillIntoAggregationQuery,
            TransactionDrillDownOutput>(query);
        if (result.Errors?.Count > 0 &&
            !result.Errors.Contains(TransactionDrillDownMessages.ProfileNotFound) &&
            !result.Errors.Contains(TransactionDrillDownMessages.BucketNotFound))
        {
            return BadRequest(result);
        }

        return ResponseResolver.Resolve(result, statusMap: new Dictionary<string, int>
        {
            [TransactionDrillDownMessages.AggregationRetrieved] = StatusCodes.Status200OK,
            [TransactionDrillDownMessages.TransactionsRetrieved] = StatusCodes.Status200OK,
            [TransactionDrillDownMessages.TransactionRetrieved] = StatusCodes.Status200OK,
            [TransactionDrillDownMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [TransactionDrillDownMessages.BucketNotFound] = StatusCodes.Status404NotFound
        });
    }
}
