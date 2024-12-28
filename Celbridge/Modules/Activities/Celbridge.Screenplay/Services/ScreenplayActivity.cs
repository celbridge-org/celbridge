using Celbridge.Activities;
using Celbridge.Documents;
using Celbridge.Entities;
using Celbridge.Explorer;
using Celbridge.Inspector;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;
using System.Text;

namespace Celbridge.Screenplay.Services;

public class ScreenplayActivity : IActivity
{
    private readonly ILogger<ScreenplayActivity> _logger;
    private readonly IEntityService _entityService;
    private readonly IInspectorService _inspectorService;
    private readonly IDocumentsService _documentService;

    public string ActivityName => ScreenplayConstants.ScreenplayActivityName;

    private HashSet<ResourceKey> _pendingEntityUpdates = new();

    public ScreenplayActivity(
        ILogger<ScreenplayActivity> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;

        _entityService = workspaceWrapper.WorkspaceService.EntityService;
        _documentService = workspaceWrapper.WorkspaceService.DocumentsService;
        _inspectorService = workspaceWrapper.WorkspaceService.InspectorService;

        messengerService.Register<SelectedResourceChangedMessage>(this, OnSelectedResourceChangedMessage);
        messengerService.Register<ComponentChangedMessage>(this, OnComponentChangedMessage);
    }

    private void OnSelectedResourceChangedMessage(object recipient, SelectedResourceChangedMessage message)
    {
        var resource = message.Resource;

        // Todo: Check for "Screenplay" entity tag instead
        if (resource.ToString().EndsWith(".md"))
        {
            _pendingEntityUpdates.Add(resource);
        }
    }

    private void OnComponentChangedMessage(object recipient, ComponentChangedMessage message)
    {
        // Todo: Check for "Screenplay" entity tag instead
        if (message.ComponentType == ScreenplayConstants.SceneComponentType ||
            message.ComponentType == ScreenplayConstants.LineComponentType)
        {
            // Regenerate the screenplay markdown on any property change to the inspected resource.
            _pendingEntityUpdates.Add(message.Resource);
        }
    }

    public async Task<Result> Start()
    {
        await Task.CompletedTask;

        return Result.Ok();
    }

    public async Task<Result> Stop()
    {
        await Task.CompletedTask;

        return Result.Ok();
    }

    public async Task<Result> UpdateAsync()
    {
        // Get the inspected entity's list of components
        bool hasError = true;
        foreach (var fileResource in _pendingEntityUpdates)
        {
            var updateResult = await UpdateEntity(fileResource);
            if (updateResult.IsFailure)
            {
                _logger.LogError(updateResult.Error);
                continue;
            }
        }
        _pendingEntityUpdates.Clear();

        if (hasError)
        {
            return Result.Fail("Failed to update a modified entity");
        }

        return Result.Ok();
    }

    public async Task<Result> UpdateEntity(ResourceKey fileResource)
    {
        var getCountResult = _entityService.GetComponentCount(fileResource);
        if (getCountResult.IsFailure)
        {
            // Inspected resource may have been deleted or moved since the update was requested
            return Result.Ok();
        }
        var componentCount = getCountResult.Value;

        // Populate the annotation data for each component associated with this activity

        bool hasScreenplayComponents = false;

        for (int i = 0; i < componentCount; i++)
        {
            // Check if the component's activityName property matches this activity's name

            var getInfoResult = _entityService.GetComponentTypeInfo(fileResource, i);
            if (getInfoResult.IsFailure)
            {
                return Result.Fail(fileResource, $"Failed to get component info for component index '{i}' on inspected resource: '{fileResource}'")
                    .WithErrors(getInfoResult);
            }
            var componentInfo = getInfoResult.Value;

            var activityName = componentInfo.GetStringAttribute("activityName");

            if (activityName != ActivityName)
            {
                // This component is not associated with this activity so we can ignore it
                continue;
            }

            hasScreenplayComponents = true;

            Result<ComponentAnnotation> getAnnotationResult = componentInfo.ComponentType switch
            {
                ScreenplayConstants.SceneComponentType => GetSceneAnnotation(fileResource, i, componentInfo),
                ScreenplayConstants.LineComponentType => GetLineAnnotation(fileResource, i, componentInfo),
                _ => Result<ComponentAnnotation>.Fail($"{nameof(ScreenplayActivity)} does not support component type '{componentInfo.ComponentType}'")
            };

            if (getAnnotationResult.IsFailure)
            {
                // Todo: Display a user-facing error messsage via the annotation data instead of returning here
                _logger.LogError(getAnnotationResult.Error);
                continue;
            }
            var annotation = getAnnotationResult.Value;

            var setAnnotationResult = _inspectorService.SetComponentAnnotation(fileResource, i, annotation);
            if (setAnnotationResult.IsFailure)
            {
                return Result.Fail($"Failed to set annotation for component index '{i}' on inspected resource: '{fileResource}'")
                    .WithErrors(setAnnotationResult);
            }
        }

        if (hasScreenplayComponents)
        {
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
        }

        await Task.CompletedTask;

        return Result.Ok();
    }

    private Result<ComponentAnnotation> GetSceneAnnotation(ResourceKey resource, int componentIndex, ComponentTypeInfo componentInfo)
    {
        var getTitleResult = _entityService.GetProperty<string>(resource, componentIndex, ScreenplayConstants.SceneComponentProperty_SceneTitle);
        if (getTitleResult.IsFailure)
        {
            return Result<ComponentAnnotation>.Fail().WithErrors(getTitleResult);
        }
        var sceneTitle = getTitleResult.Value;

        var getDescriptionResult = _entityService.GetProperty<string>(resource, componentIndex, ScreenplayConstants.SceneComponentProperty_SceneDescription);
        if (getDescriptionResult.IsFailure)
        {
            return Result<ComponentAnnotation>.Fail().WithErrors(getDescriptionResult);
        }
        var sceneDescription = getDescriptionResult.Value;

        // Todo: Use a localized string to format this
        var componentDescription = $"{sceneTitle}: {sceneDescription}";

        var annotation = new ComponentAnnotation(componentDescription);

        return Result<ComponentAnnotation>.Ok(annotation);
    }

    private Result<ComponentAnnotation> GetLineAnnotation(ResourceKey resource, int componentIndex, ComponentTypeInfo componentInfo)
    {
        var getCharacterResult = _entityService.GetProperty<string>(resource, componentIndex, ScreenplayConstants.LineComponentProperty_Character);
        if (getCharacterResult.IsFailure)
        {
            return Result<ComponentAnnotation>.Fail().WithErrors(getCharacterResult);
        }
        var character = getCharacterResult.Value;

        var getSourceTextResult = _entityService.GetProperty<string>(resource, componentIndex, ScreenplayConstants.LineComponentProperty_SourceText);
        if (getSourceTextResult.IsFailure)
        {
            return Result<ComponentAnnotation>.Fail().WithErrors(getSourceTextResult);
        }
        var sourceText = getSourceTextResult.Value;

        // Todo: Use a localized string to format this
        var description = $"{character}: {sourceText}";

        var annotation = new ComponentAnnotation(description);

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

        var getTitleResult = _entityService.GetProperty<string>(resource, sceneComponentIndex, ScreenplayConstants.SceneComponentProperty_SceneTitle);
        if (getTitleResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to get scene title")
                .WithErrors(getTitleResult);
        }
        var sceneTitle = getTitleResult.Value;

        var getDescriptionResult = _entityService.GetProperty<string>(resource, sceneComponentIndex, ScreenplayConstants.SceneComponentProperty_SceneDescription);
        if (getDescriptionResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to get scene description")
                .WithErrors(getDescriptionResult);
        }
        var sceneDescription = getDescriptionResult.Value;

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
            var getCharacterResult = _entityService.GetProperty<string>(resource, lineComponentIndex, ScreenplayConstants.LineComponentProperty_Character);
            if (getCharacterResult.IsFailure)
            {
                return Result<string>.Fail($"Failed to get character")
                    .WithErrors(getCharacterResult);
            }
            var character = getCharacterResult.Value;

            var getSourceTextResult = _entityService.GetProperty<string>(resource, lineComponentIndex, ScreenplayConstants.LineComponentProperty_SourceText);
            if (getSourceTextResult.IsFailure)
            {
                return Result<string>.Fail($"Failed to get source text")
                    .WithErrors(getSourceTextResult);
            }
            var sourceText = getSourceTextResult.Value;

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
