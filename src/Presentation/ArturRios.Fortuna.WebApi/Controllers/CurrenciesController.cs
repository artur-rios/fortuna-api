using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Query;
using ArturRios.Output;
using ArturRios.Util.WebApi.AspNetCore;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/currencies")]
public sealed class CurrenciesController(QueryMediator queryMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [CurrencyMessages.CurrencyNotFound] = StatusCodes.Status404NotFound
        };

    [HttpGet]
    public async Task<ActionResult<DataOutput<ListSupportedCurrenciesQueryOutput?>>> List()
    {
        var result = await queryMediator.ExecuteQueryAsync<
            ListSupportedCurrenciesQuery,
            ListSupportedCurrenciesQueryOutput>(new ListSupportedCurrenciesQuery());

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet("{code}")]
    public async Task<ActionResult<DataOutput<CurrencyOutput?>>> GetByCode(string code)
    {
        var result = await queryMediator.ExecuteQueryAsync<GetCurrencyByCodeQuery, CurrencyOutput>(
            new GetCurrencyByCodeQuery { Code = code });

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
