using Celbridge.Activities;
using Celbridge.Entities;
using Celbridge.Explorer;
using Celbridge.Notes.ComponentEditors;
using Celbridge.Workspace;

namespace Celbridge.Notes.Services;

public class NoteActivity : IActivity
{
    public const string ActivityName = "Notes";

    private readonly IEntityService _entityService;

    public NoteActivity(
        IWorkspaceWrapper workspaceWrapper)
    {
        _entityService = workspaceWrapper.WorkspaceService.EntityService;
    }

    public async Task<Result> ActivateAsync()
    {
        await Task.CompletedTask;

        return Result.Ok();
    }

    public async Task<Result> DeactivateAsync()
    {
        await Task.CompletedTask;
        return Result.Ok();
    }

    public bool SupportsResource(ResourceKey resource)
    {
        var extension = Path.GetExtension(resource);
        return extension == ExplorerConstants.NoteExtension;
    }

    public async Task<Result> InitializeResourceAsync(ResourceKey resource)
    {
        if (!SupportsResource(resource))
        {
            return Result.Fail($"This activity does not support this resource: {resource}");
        }

        var count = _entityService.GetComponentCount(resource);
        if (count > 0)
        {
            // Entity has already been initialized
            return Result.Ok();
        }

        _entityService.AddComponent(new ComponentKey(resource, 0), NoteEditor.ComponentType);

        await Task.CompletedTask;

        return Result.Ok();
    }

    public Result AnnotateEntity(ResourceKey entity, IEntityAnnotation entityAnnotation)
    {
        var getComponents = _entityService.GetComponents(entity);
        if (getComponents.IsFailure)
        {
            return Result.Fail(entity, $"Failed to get entity components: '{entity}'")
                .WithErrors(getComponents);
        }
        var components = getComponents.Value;

        if (components.Count != entityAnnotation.ComponentAnnotationCount)
        {
            return Result.Fail(entity, $"Component count does not match annotation count: '{entity}'");
        }

        //
        // Root component must be "Note"
        //

        var rootComponent = components[0];
        if (rootComponent.IsComponentType(NoteEditor.ComponentType))
        {
            entityAnnotation.SetIsRecognized(0);
        }

        return Result.Ok();
    }

    public async Task<Result> UpdateResourceContentAsync(ResourceKey fileResource, IEntityAnnotation entityAnnotation)
    {
        await Task.CompletedTask;

        return Result.Ok();
    }
}
