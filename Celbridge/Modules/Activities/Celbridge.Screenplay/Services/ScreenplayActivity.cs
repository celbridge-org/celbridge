using Celbridge.Activities;
using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Entities;
using Celbridge.Logging;
using Celbridge.Screenplay.Components;
using Celbridge.Workspace;
using System.Text;

using Path = System.IO.Path;

namespace Celbridge.Screenplay.Services;

public class ScreenplayActivity : IActivity
{
    private readonly ILogger<ScreenplayActivity> _logger;
    private readonly ICommandService _commandService;
    private readonly IEntityService _entityService;
    private readonly IDocumentsService _documentService;

    private HashSet<ResourceKey> _pendingEntityUpdates = new();

    public ScreenplayActivity(
        ILogger<ScreenplayActivity> logger,        
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
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

        var getCountResult = _entityService.GetComponentCount(resource);
        if (getCountResult.IsFailure)
        {
            return Result.Fail($"Failed to get component count for resource: '{resource}'")
                .WithErrors(getCountResult);
        }
        var count = getCountResult.Value;

        if (count > 0)
        {
            // Entity has already been initialized
            return Result.Ok();
        }

        _entityService.AddComponent(resource, 0, SceneComponent.ComponentType);

        await Task.CompletedTask;

        return Result.Ok();
    }

    public async Task<Result> UpdateResourceAsync(ResourceKey fileResource)
    {
        var getCountResult = _entityService.GetComponentCount(fileResource);
        if (getCountResult.IsFailure)
        {
            // Inspected resource may have been deleted or moved since the update was requested
            return Result.Ok();
        }
        var componentCount = getCountResult.Value;

        // Populate the annotation data for each component associated with this activity

        for (int i = 0; i < componentCount; i++)
        {
            // Get the component 
            var getComponentResult = _entityService.GetComponent(fileResource, i);
            if (getComponentResult.IsFailure)
            {
                return Result.Fail(fileResource, $"Failed to get component for resource '{fileResource}' at index {i}")
                    .WithErrors(getComponentResult);
            }
            var component = getComponentResult.Value;

            var schema = component.Schema;

            if (schema.ComponentType == "Empty")
            {
                // Ignore empty components
                continue;
            }

            if (!schema.HasTag(ScreenplayActivityComponent.ActivityName))
            {
                component.SetAnnotation(
                    ComponentStatus.Error,
                    "Not a screenplay component",
                    "This component may not be used with the 'Screenplay' activity");

                continue;
            }

            switch (schema.ComponentType)
            {
                case SceneComponent.ComponentType:
                    var sceneTitle = component.GetString(SceneComponent.SceneTitle);
                    var sceneDescription = component.GetString(SceneComponent.SceneDescription);
                    var componentDescription = $"{sceneTitle}: {sceneDescription}";
                    component.SetAnnotation(ComponentStatus.Valid, componentDescription, componentDescription);
                    break;

                case LineComponent.ComponentType:
                    var character = component.GetString(LineComponent.Character);
                    var sourceText = component.GetString(LineComponent.SourceText);
                    var description = $"{character}: {sourceText}";
                    component.SetAnnotation(ComponentStatus.Valid, description, description);
                    break;
            };
        }

        var generateResult = GenerateScreenplayMarkdown(fileResource);
        if (generateResult.IsFailure)
        {
            return Result.Fail($"Failed to generate screenplay markdown").
                WithErrors(generateResult);
        }

        var markdown = generateResult.Value;

        // Set the contents of the document to the generated markdown
        var setContentResult = _documentService.SetTextDocumentContent(fileResource, markdown);
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
        var getComponentResult = _entityService.GetComponentOfType(resource, SceneComponent.ComponentType);
        if (getComponentResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to get Scene component")
                .WithErrors(getComponentResult);
        }
        var sceneComponent = getComponentResult.Value;

        var sceneTitle = sceneComponent.GetString(SceneComponent.SceneTitle);
        var sceneDescription = sceneComponent.GetString(SceneComponent.SceneDescription);

        var sb = new StringBuilder();

        sb.AppendLine($"# {sceneTitle}");
        sb.AppendLine();
        sb.AppendLine($"{sceneDescription}");
        sb.AppendLine();

        var getLinesResult = _entityService.GetComponentsOfType(resource, LineComponent.ComponentType);
        if (getLinesResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to get Line components")
                .WithErrors(getLinesResult);
        }
        var lineComponents = getLinesResult.Value;

        foreach (var lineComponent in lineComponents)
        {
            var character = lineComponent.GetString(LineComponent.Character);
            var sourceText = lineComponent.GetString(LineComponent.SourceText);

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
