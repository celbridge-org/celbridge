using Celbridge.Activities;
using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Entities;
using Celbridge.Logging;
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

    public async Task<Result> UpdateEntityAsync(ResourceKey fileResource)
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
            // Get the component type
            var getTypeResult = _entityService.GetComponentType(fileResource, i);
            if (getTypeResult.IsFailure)
            {
                return Result.Fail(fileResource, $"Failed to get component type for resource '{fileResource}' at component index {i}")
                    .WithErrors(getTypeResult);
            }
            var componentType = getTypeResult.Value;

            // Get the component schema
            var getSchemaResult = _entityService.GetComponentSchema(componentType);
            if (getSchemaResult.IsFailure)
            {
                return Result.Fail(fileResource, $"Failed to get component schema for component type: '{componentType}'")
                    .WithErrors(getSchemaResult);
            }
            var schema = getSchemaResult.Value;

            if (componentType == "Empty")
            {
                // Ignore comments
                continue;
            }

            if (!schema.HasTag(ScreenplayConstants.ScreenplayTag))
            {
                // Not a Screenplay component

                var errorAnnotation = new ComponentAnnotation(
                    ComponentStatus.Error, 
                    "Not a scene component", 
                    "This component may not be used with the 'Scene' primary component");

                _entityService.UpdateComponentAnnotation(fileResource, i, errorAnnotation);
                continue;
            }

            Result<ComponentAnnotation> getAnnotationResult = schema.ComponentType switch
            {
                ScreenplayConstants.SceneComponentType => GetSceneAnnotation(fileResource, i),
                ScreenplayConstants.LineComponentType => GetLineAnnotation(fileResource, i),
                _ => Result<ComponentAnnotation>.Fail($"{nameof(ScreenplayActivity)} does not support component type '{schema.ComponentType}'")
            };

            if (getAnnotationResult.IsFailure)
            {
                // Todo: Display a user-facing error messsage via the annotation data instead of returning here
                _logger.LogError(getAnnotationResult.Error);
                continue;
            }
            var annotation = getAnnotationResult.Value;

            var updateAnnotationResult = _entityService.UpdateComponentAnnotation(fileResource, i, annotation);
            if (updateAnnotationResult.IsFailure)
            {
                return Result.Fail($"Failed to update annotation for component index '{i}' on inspected resource: '{fileResource}'")
                    .WithErrors(updateAnnotationResult);
            }
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

    public bool TryInitializeEntity(ResourceKey resource)
    {
        var extension = Path.GetExtension(resource);
        if (extension == ".scene")
        {
            // Add a Scene component to newly created .scene entities
            _commandService.Execute<IAddComponentCommand>(command => {
                command.Resource = resource;
                command.ComponentType = ScreenplayConstants.SceneComponentType;
            });
            return true;
        }

        return false;
    }

    private Result<ComponentAnnotation> GetSceneAnnotation(ResourceKey resource, int componentIndex)
    {
        var sceneTitle = _entityService.GetString(resource, componentIndex, ScreenplayConstants.SceneComponent_SceneTitle);
        var sceneDescription = _entityService.GetString(resource, componentIndex, ScreenplayConstants.SceneComponent_SceneDescription);

        // Todo: Use a localized string to format this
        var componentDescription = $"{sceneTitle}: {sceneDescription}";

        var annotation = new ComponentAnnotation(ComponentStatus.Valid, componentDescription, componentDescription);

        return Result<ComponentAnnotation>.Ok(annotation);
    }

    private Result<ComponentAnnotation> GetLineAnnotation(ResourceKey resource, int componentIndex)
    {
        var character = _entityService.GetString(resource, componentIndex, ScreenplayConstants.LineComponent_Character);
        var sourceText = _entityService.GetString(resource, componentIndex, ScreenplayConstants.LineComponent_SourceText);

        // Todo: Use a localized string to format this
        var description = $"{character}: {sourceText}";

        var annotation = new ComponentAnnotation(ComponentStatus.Valid, description, description);

        return Result<ComponentAnnotation>.Ok(annotation);
    }

    private Result<string> GenerateScreenplayMarkdown(ResourceKey resource)
    {
        var getSceneResult = _entityService.GetComponentsOfType(resource, ScreenplayConstants.SceneComponentType);
        if (getSceneResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to get Scene component")
                .WithErrors(getSceneResult);
        }
        var sceneIndices = getSceneResult.Value;

        if (sceneIndices.Count != 1)
        {
            return Result<string>.Fail($"Failed to get Scene component");
        }

        var sceneComponentIndex = sceneIndices[0];

        var sceneTitle = _entityService.GetString(resource, sceneComponentIndex, ScreenplayConstants.SceneComponent_SceneTitle);
        var sceneDescription = _entityService.GetString(resource, sceneComponentIndex, ScreenplayConstants.SceneComponent_SceneDescription);

        var sb = new StringBuilder();

        sb.AppendLine($"# {sceneTitle}");
        sb.AppendLine();
        sb.AppendLine($"{sceneDescription}");
        sb.AppendLine();

        var getLinesResult = _entityService.GetComponentsOfType(resource, ScreenplayConstants.LineComponentType);
        if (getLinesResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to get Line components")
                .WithErrors(getLinesResult);
        }
        var lineComponentIndices = getLinesResult.Value;

        foreach (var lineComponentIndex in lineComponentIndices)
        {
            var character = _entityService.GetString(resource, lineComponentIndex, ScreenplayConstants.LineComponent_Character);
            var sourceText = _entityService.GetString(resource, lineComponentIndex, ScreenplayConstants.LineComponent_SourceText);

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
