using Celbridge.Activities;
using Celbridge.Entities;
using Celbridge.HTML.Components;
using Celbridge.Workspace;

namespace Celbridge.HTML.Services;

public class HTMLActivity : IActivity
{
    private readonly IEntityService _entityService;

    public HTMLActivity(IWorkspaceWrapper workspaceWrapper)
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

    public async Task<Result> InitializeResourceAsync(ResourceKey resource)
    {
        await Task.CompletedTask;
        return Result.Ok();
    }

    public bool SupportsResource(ResourceKey resource)
    {
        var extension = Path.GetExtension(resource);
        return extension == ".html";
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
        // Root component must be "HTML"
        //

        var sceneComponent = components[0];
        if (sceneComponent.IsComponentType(HTMLEditor.ComponentType))
        {
            entityAnnotation.SetIsRecognized(0);
        }
        else
        {
            var error = new AnnotationError(
                AnnotationErrorSeverity.Error,
                "Invalid component position",
                "This component must be the first component.");

            entityAnnotation.AddComponentError(0, error);
        }

        return Result.Ok();
    }

    public async Task<Result> UpdateResourceContentAsync(ResourceKey resource, IEntityAnnotation entityAnnotation)
    {
        await Task.CompletedTask;

        return Result.Ok();
    }
}
