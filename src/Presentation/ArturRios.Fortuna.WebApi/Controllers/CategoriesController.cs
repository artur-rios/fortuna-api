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
[Route("api/categories")]
public sealed class CategoriesController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [CategoryMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [CategoryMessages.UpdatedSuccessfully] = StatusCodes.Status200OK,
            [CategoryMessages.TransactionsReassignedSuccessfully] = StatusCodes.Status200OK,
            [CategoryMessages.DeletedSuccessfully] = StatusCodes.Status200OK,
            [CategoryMessages.RestoredSuccessfully] = StatusCodes.Status200OK,
            [CategoryMessages.HardDeletedSuccessfully] = StatusCodes.Status200OK,
            [CategoryMessages.NotFound] = StatusCodes.Status404NotFound,
            [CategoryMessages.ParentNotFound] = StatusCodes.Status404NotFound,
            [CategoryMessages.DuplicateSiblingName] = StatusCodes.Status409Conflict,
            [CategoryMessages.CycleDetected] = StatusCodes.Status409Conflict,
            [CategoryMessages.NameRequired] = StatusCodes.Status400BadRequest,
            [CategoryMessages.NameTooLong] = StatusCodes.Status400BadRequest,
            [CategoryMessages.ParentIdInvalid] = StatusCodes.Status400BadRequest,
            [CategoryMessages.TargetCategoryIdInvalid] = StatusCodes.Status400BadRequest,
            [CategoryMessages.SourceAndTargetMustDiffer] = StatusCodes.Status400BadRequest,
            [CategoryMessages.RestoreRequiresSoftDeletion] = StatusCodes.Status409Conflict,
            [CategoryMessages.HardDeleteRequiresSoftDeletion] = StatusCodes.Status409Conflict,
            [CategoryMessages.HardDeleteHasLiveTransactions] = StatusCodes.Status409Conflict,
            [CategoryMessages.HardDeleteHasDependents] = StatusCodes.Status409Conflict
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CreateCategoryCommandOutput?>>> Create(
        [FromBody] CreateCategoryCommand command)
    {
        return await SendAsync<
            CreateCategoryCommand,
            CreateCategoryCommandOutput>(command);
    }

    [HttpPut("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<UpdateCategoryCommandOutput?>>> Update(
        Guid id,
        [FromBody] UpdateCategoryCommand command)
    {
        command.Id = id;

        return await SendAsync<
            UpdateCategoryCommand,
            UpdateCategoryCommandOutput>(command);
    }

    [HttpPost("{id:guid}/reassign")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<ReassignCategoryTransactionsCommandOutput?>>> Reassign(
        Guid id,
        [FromBody] ReassignCategoryTransactionsCommand command)
    {
        command.Id = id;

        return await SendAsync<
            ReassignCategoryTransactionsCommand,
            ReassignCategoryTransactionsCommandOutput>(command);
    }

    [HttpDelete("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CategoryLifecycleCommandOutput?>>> Delete(Guid id)
    {
        return await SendAsync<
            DeleteCategoryCommand,
            CategoryLifecycleCommandOutput>(new DeleteCategoryCommand { Id = id });
    }

    [HttpPost("{id:guid}/restore")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CategoryLifecycleCommandOutput?>>> Restore(Guid id)
    {
        return await SendAsync<
            RestoreCategoryCommand,
            CategoryLifecycleCommandOutput>(new RestoreCategoryCommand { Id = id });
    }

    [HttpDelete("{id:guid}/hard")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CategoryLifecycleCommandOutput?>>> HardDelete(Guid id)
    {
        return await SendAsync<
            HardDeleteCategoryCommand,
            CategoryLifecycleCommandOutput>(new HardDeleteCategoryCommand { Id = id });
    }

    [HttpGet]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CategoryTreeOutput?>>> GetTree(
        [FromQuery] bool includeDeleted = false,
        [FromQuery] bool includeUsageCounts = false)
    {
        return await QueryAsync<
            GetCategoryTreeQuery,
            CategoryTreeOutput>(new GetCategoryTreeQuery
            {
                IncludeDeleted = includeDeleted,
                IncludeUsageCounts = includeUsageCounts
            });
    }

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CategoryOutput?>>> GetById(
        Guid id,
        [FromQuery] bool includeDeleted = false,
        [FromQuery] bool includeUsageCounts = false)
    {
        return await QueryAsync<
            GetCategoryByIdQuery,
            CategoryOutput>(new GetCategoryByIdQuery
            {
                Id = id,
                IncludeDeleted = includeDeleted,
                IncludeUsageCounts = includeUsageCounts
            });
    }
}
