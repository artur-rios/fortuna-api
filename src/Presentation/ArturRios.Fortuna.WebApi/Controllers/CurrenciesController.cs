using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/currencies")]
public sealed class CurrenciesController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [CurrencyMessages.CurrencyNotFound] = StatusCodes.Status404NotFound
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpGet]
    public async Task<ActionResult<DataOutput<ListSupportedCurrenciesQueryOutput?>>> List()
    {
        return await QueryAsync<
            ListSupportedCurrenciesQuery,
            ListSupportedCurrenciesQueryOutput>(new ListSupportedCurrenciesQuery());
    }

    [HttpGet("{code}")]
    public async Task<ActionResult<DataOutput<CurrencyOutput?>>> GetByCode(string code)
    {
        return await QueryAsync<GetCurrencyByCodeQuery, CurrencyOutput>(
            new GetCurrencyByCodeQuery { Code = code });
    }
}
