using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/reports")]
public sealed class ReportsController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [TableReportMessages.RetrievedSuccessfully] = StatusCodes.Status200OK,
            [TableReportMessages.RecordSetRequired] = StatusCodes.Status400BadRequest,
            [TableReportMessages.ColumnsRequired] = StatusCodes.Status400BadRequest,
            [TableReportMessages.ColumnsMustBeUnique] = StatusCodes.Status400BadRequest,
            [TableReportMessages.InvalidPageNumber] = StatusCodes.Status400BadRequest,
            [TableReportMessages.InvalidPageSize] = StatusCodes.Status400BadRequest,
            [TableReportMessages.FilterFieldRequired] = StatusCodes.Status400BadRequest,
            [TableReportMessages.FilterOperatorRequired] = StatusCodes.Status400BadRequest,
            [TableReportMessages.FilterValueRequired] = StatusCodes.Status400BadRequest,
            [TableReportMessages.SortFieldRequired] = StatusCodes.Status400BadRequest,
            [TableReportMessages.FiltersRequired] = StatusCodes.Status400BadRequest,
            [TableReportMessages.FilterRequired] = StatusCodes.Status400BadRequest,
            [TableReportMessages.SortsRequired] = StatusCodes.Status400BadRequest,
            [TableReportMessages.SortRequired] = StatusCodes.Status400BadRequest,
            [TableReportMessages.DisplayCurrencyInvalid] = StatusCodes.Status400BadRequest,
            [TableReportMessages.DisplayCurrencyUnsupported] = StatusCodes.Status400BadRequest
        });

    private static readonly IReadOnlyDictionary<string, int> AggregationStatuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [TransactionAggregationMessages.RetrievedSuccessfully] = StatusCodes.Status200OK
        });

    private static readonly IReadOnlyDictionary<string, int> DrillDownStatuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [TransactionDrillDownMessages.AggregationRetrieved] = StatusCodes.Status200OK,
            [TransactionDrillDownMessages.TransactionsRetrieved] = StatusCodes.Status200OK,
            [TransactionDrillDownMessages.TransactionRetrieved] = StatusCodes.Status200OK,
            [TransactionDrillDownMessages.BucketNotFound] = StatusCodes.Status404NotFound
        });

    private static readonly IReadOnlyDictionary<string, int> NetPositionStatuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [NetPositionMessages.RetrievedSuccessfully] = StatusCodes.Status200OK,
            [NetPositionMessages.PartiallyConverted] = StatusCodes.Status200OK,
            [NetPositionMessages.AsOfOutOfRange] = StatusCodes.Status400BadRequest
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpPost("table")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<TableReportOutput?>>> QueryTable(
        [FromBody] QueryRecordsAsTableQuery query)
    {
        return await QueryAsync<
            QueryRecordsAsTableQuery,
            TableReportOutput>(query);
    }

    [HttpGet("aggregate")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<TransactionAggregationOutput?>>> Aggregate(
        [FromQuery] AggregateTransactionsQuery query)
    {
        return await QueryAsync<
            AggregateTransactionsQuery,
            TransactionAggregationOutput>(query, AggregationStatuses);
    }

    [HttpGet("drill-down")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<TransactionDrillDownOutput?>>> DrillDown(
        [FromQuery] DrillIntoAggregationQuery query)
    {
        return await QueryAsync<
            DrillIntoAggregationQuery,
            TransactionDrillDownOutput>(query, DrillDownStatuses);
    }

    [HttpGet("net-position")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<NetPositionOutput?>>> NetPosition(
        [FromQuery] GetNetPositionQuery query)
    {
        return await QueryAsync<GetNetPositionQuery, NetPositionOutput>(query, NetPositionStatuses);
    }
}
