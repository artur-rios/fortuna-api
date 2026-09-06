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
[Route("api/counterparties")]
public sealed class CounterpartiesController(
    CommandMediator commandMediator,
    QueryMediator queryMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [CounterpartyMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [CounterpartyMessages.ReusedSuccessfully] = StatusCodes.Status200OK,
            [CounterpartyMessages.UpdatedSuccessfully] = StatusCodes.Status200OK,
            [CounterpartyMessages.DeletedSuccessfully] = StatusCodes.Status200OK,
            [CounterpartyMessages.MergedSuccessfully] = StatusCodes.Status200OK,
            [CounterpartyMessages.ListedSuccessfully] = StatusCodes.Status200OK,
            [CounterpartyMessages.SuggestedSuccessfully] = StatusCodes.Status200OK,
            [CounterpartyMessages.NoSuggestion] = StatusCodes.Status200OK,
            [CounterpartyMessages.NotFound] = StatusCodes.Status404NotFound,
            [CounterpartyMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [CounterpartyMessages.DuplicateName] = StatusCodes.Status409Conflict,
            [CounterpartyMessages.SameCounterparty] = StatusCodes.Status400BadRequest,
            [CounterpartyMessages.NameRequired] = StatusCodes.Status400BadRequest,
            [CounterpartyMessages.NameTooLong] = StatusCodes.Status400BadRequest,
            [CounterpartyMessages.TargetIdInvalid] = StatusCodes.Status400BadRequest
        };

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CounterpartyCommandOutput?>>> Create(
        [FromBody] CreateCounterpartyCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            CreateCounterpartyCommand,
            CounterpartyCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CounterpartyListOutput?>>> List(
        [FromQuery] bool includeDeleted = false)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            ListCounterpartiesQuery,
            CounterpartyListOutput>(new ListCounterpartiesQuery
            {
                IncludeDeleted = includeDeleted
            });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPut("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CounterpartyCommandOutput?>>> Update(
        Guid id,
        [FromBody] UpdateCounterpartyCommand command)
    {
        command.Id = id;
        var result = await commandMediator.ExecuteCommandAsync<
            UpdateCounterpartyCommand,
            CounterpartyCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpDelete("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CounterpartyCommandOutput?>>> Delete(Guid id)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            DeleteCounterpartyCommand,
            CounterpartyCommandOutput>(new DeleteCounterpartyCommand { Id = id });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("{id:guid}/merge")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CounterpartyMergeCommandOutput?>>> Merge(
        Guid id,
        [FromBody] MergeCounterpartiesCommand command)
    {
        command.Id = id;
        var result = await commandMediator.ExecuteCommandAsync<
            MergeCounterpartiesCommand,
            CounterpartyMergeCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet("{id:guid}/suggested-category")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CounterpartyCategorySuggestionOutput?>>> SuggestCategory(
        Guid id)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            SuggestCounterpartyCategoryQuery,
            CounterpartyCategorySuggestionOutput>(new SuggestCounterpartyCategoryQuery { Id = id });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
