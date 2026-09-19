using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/counterparties")]
public sealed class CounterpartiesController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [CounterpartyMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [CounterpartyMessages.ReusedSuccessfully] = StatusCodes.Status200OK,
            [CounterpartyMessages.UpdatedSuccessfully] = StatusCodes.Status200OK,
            [CounterpartyMessages.DeletedSuccessfully] = StatusCodes.Status200OK,
            [CounterpartyMessages.MergedSuccessfully] = StatusCodes.Status200OK,
            [CounterpartyMessages.ListedSuccessfully] = StatusCodes.Status200OK,
            [CounterpartyMessages.InvalidPageNumber] = StatusCodes.Status400BadRequest,
            [CounterpartyMessages.InvalidPageSize] = StatusCodes.Status400BadRequest,
            [CounterpartyMessages.SuggestedSuccessfully] = StatusCodes.Status200OK,
            [CounterpartyMessages.NoSuggestion] = StatusCodes.Status200OK,
            [CounterpartyMessages.NotFound] = StatusCodes.Status404NotFound,
            [CounterpartyMessages.DuplicateName] = StatusCodes.Status409Conflict,
            [CounterpartyMessages.SameCounterparty] = StatusCodes.Status400BadRequest,
            [CounterpartyMessages.NameRequired] = StatusCodes.Status400BadRequest,
            [CounterpartyMessages.NameTooLong] = StatusCodes.Status400BadRequest,
            [CounterpartyMessages.TargetIdInvalid] = StatusCodes.Status400BadRequest
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CounterpartyCommandOutput?>>> Create(
        [FromBody] CreateCounterpartyCommand command)
    {
        return await SendAsync<
            CreateCounterpartyCommand,
            CounterpartyCommandOutput>(command);
    }

    [HttpGet]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CounterpartyListOutput?>>> List(
        [FromQuery] bool includeDeleted = false,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 100)
    {
        return await QueryAsync<
            ListCounterpartiesQuery,
            CounterpartyListOutput>(new ListCounterpartiesQuery
            {
                IncludeDeleted = includeDeleted,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
    }

    [HttpPut("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CounterpartyCommandOutput?>>> Update(
        Guid id,
        [FromBody] UpdateCounterpartyCommand command)
    {
        command.Id = id;

        return await SendAsync<
            UpdateCounterpartyCommand,
            CounterpartyCommandOutput>(command);
    }

    [HttpDelete("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CounterpartyCommandOutput?>>> Delete(Guid id)
    {
        return await SendAsync<
            DeleteCounterpartyCommand,
            CounterpartyCommandOutput>(new DeleteCounterpartyCommand { Id = id });
    }

    [HttpPost("{id:guid}/merge")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CounterpartyMergeCommandOutput?>>> Merge(
        Guid id,
        [FromBody] MergeCounterpartiesCommand command)
    {
        command.Id = id;

        return await SendAsync<
            MergeCounterpartiesCommand,
            CounterpartyMergeCommandOutput>(command);
    }

    [HttpGet("{id:guid}/suggested-category")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CounterpartyCategorySuggestionOutput?>>> SuggestCategory(
        Guid id)
    {
        return await QueryAsync<
            SuggestCounterpartyCategoryQuery,
            CounterpartyCategorySuggestionOutput>(new SuggestCounterpartyCategoryQuery { Id = id });
    }
}
