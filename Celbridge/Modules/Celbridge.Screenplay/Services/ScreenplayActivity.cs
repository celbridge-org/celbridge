using Celbridge.Activities;
using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Entities;
using Celbridge.Logging;
using Celbridge.Screenplay.Components;
using Celbridge.Workspace;
using System.ComponentModel;
using System.Text;

using Path = System.IO.Path;

namespace Celbridge.Screenplay.Services;

public class ScreenplayActivity : IActivity
{
    public const string ActivityName = "Screenplay";

    private const string EmptyComponentType = ".Empty";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ScreenplayActivity> _logger;
    private readonly ICommandService _commandService;
    private readonly IEntityService _entityService;
    private readonly IDocumentsService _documentService;

    private HashSet<ResourceKey> _pendingEntityUpdates = new();

    public ScreenplayActivity(
        IServiceProvider serviceProvider,
        ILogger<ScreenplayActivity> logger,        
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _commandService = commandService;
        _entityService = workspaceWrapper.WorkspaceService.EntityService;
        _documentService = workspaceWrapper.WorkspaceService.DocumentsService;
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
        return extension == ".scene";
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

        _entityService.AddComponent(new ComponentKey(resource, 0), SceneEditor.ComponentType);

        await Task.CompletedTask;

        return Result.Ok();
    }

    public Result UpdateEntityAnnotation(ResourceKey entity, IEntityAnnotation entityAnnotation)
    {
        var getComponents = _entityService.GetComponents(entity);
        if (getComponents.IsFailure)
        {
            return Result.Fail(entity, $"Failed to get entity components: '{entity}'")
                .WithErrors(getComponents);
        }
        var components = getComponents.Value;

        if (components.Count != entityAnnotation.Count)
        {
            return Result.Fail(entity, $"Component count does not match annotation count: '{entity}'");
        }

        //
        // Root component must be "Scene"
        //

        var rootComponent = components[0];
        if (rootComponent.Schema.ComponentType == SceneEditor.ComponentType)
        {
            entityAnnotation.SetIsRecognized(0);
        }
        else
        {
            var error = new ComponentError(
                ComponentErrorSeverity.Critical,
                "Invalid component position",
                "This component must be the first component.");

            entityAnnotation.AddError(0, error);
        }

        //
        // Remaining components must be "Line"
        //

        for (int i = 1; i < components.Count; i++)
        {
            var component = components[i];

            if (component.Schema.ComponentType == ".Empty")
            {
                // Skip empty components
                continue;
            }

            if (component.Schema.ComponentType == LineEditor.ComponentType)
            {
                entityAnnotation.SetIsRecognized(i);
            }
            else
            {
                var error = new ComponentError(
                    ComponentErrorSeverity.Critical,
                    "Invalid component type",
                    "This component must be a 'Line' component");

                entityAnnotation.AddError(i, error);
            }
        }

        return Result.Ok();
    }

    public async Task<Result> UpdateResourceAsync(ResourceKey resource)
    {
        var count = _entityService.GetComponentCount(resource);
        if (count == 0)
        {
            // Resource may have been deleted or moved since the update was requested
            return Result.Ok();
        }

        var generateResult = GenerateScreenplayMarkdown(resource);
        if (generateResult.IsFailure)
        {
            return Result.Fail($"Failed to generate screenplay markdown").
                WithErrors(generateResult);
        }

        var markdown = generateResult.Value;

        // Set the contents of the document to the generated markdown
        var setContentResult = _documentService.SetTextDocumentContent(resource, markdown);
        if (setContentResult.IsFailure)
        {
            return Result.Fail($"Failed to set document content")
                .WithErrors(setContentResult);
        }

        await Task.CompletedTask;

        return Result.Ok();
    }

    private Result<string> GenerateScreenplayMarkdown(ResourceKey resource)
    {
        var getComponentResult = _entityService.GetComponentOfType(resource, SceneEditor.ComponentType);
        if (getComponentResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to get Scene component")
                .WithErrors(getComponentResult);
        }
        var sceneComponent = getComponentResult.Value;

        var sceneTitle = sceneComponent.GetString(SceneEditor.SceneTitle);
        var sceneDescription = sceneComponent.GetString(SceneEditor.SceneDescription);

        var sb = new StringBuilder();

        sb.AppendLine($"# {sceneTitle}");
        sb.AppendLine();
        sb.AppendLine($"{sceneDescription}");
        sb.AppendLine();

        var getLinesResult = _entityService.GetComponentsOfType(resource, LineEditor.ComponentType);
        if (getLinesResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to get Line components")
                .WithErrors(getLinesResult);
        }
        var lineComponents = getLinesResult.Value;

        foreach (var lineComponent in lineComponents)
        {
            var character = lineComponent.GetString(LineEditor.Character);
            var sourceText = lineComponent.GetString(LineEditor.SourceText);

            if (string.IsNullOrWhiteSpace(character) || string.IsNullOrWhiteSpace(sourceText))
            {
                continue;
            }

            sb.AppendLine($"**{character}**: {sourceText}");
            sb.AppendLine();
        }

        var markdown = sb.ToString();

        return Result<string>.Ok(markdown);
    }
}
