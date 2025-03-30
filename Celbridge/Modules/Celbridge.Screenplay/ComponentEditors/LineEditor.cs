using System.Text;
using System.Text.Json;
using Celbridge.Activities;
using Celbridge.Entities;
using Celbridge.Screenplay.Services;
using Celbridge.Workspace;

namespace Celbridge.Screenplay.Components;

public class LineEditor : ComponentEditorBase
{
    private const string _configPath = "Celbridge.Screenplay.Assets.Components.LineComponent.json";
    private const string _formPath = "Celbridge.Screenplay.Assets.Forms.LineForm.json";

    public const string ComponentType = "Screenplay.Line";
    public const string DialogueKey = "/dialogueKey";
    public const string CharacterId = "/characterId";
    public const string SpeakingTo = "/speakingTo";
    public const string SourceText = "/sourceText";
    public const string ContextNotes = "/contextNotes";
    public const string Direction = "/direction";

    private readonly IEntityService _entityService;
    private readonly IActivityService _activityService;

    public LineEditor(IWorkspaceWrapper workspaceWrapper)
    {
        _entityService = workspaceWrapper.WorkspaceService.EntityService;
        _activityService = workspaceWrapper.WorkspaceService.ActivityService;
    }

    public override string GetComponentConfig()
    {
        return LoadEmbeddedResource(_configPath);
    }

    public override string GetComponentForm()
    {
        return LoadEmbeddedResource(_formPath);
    }

    public override ComponentSummary GetComponentSummary()
    {
        var dialogueKey = Component.GetString(DialogueKey);
        var characterId = Component.GetString(CharacterId);
        var speakingTo = Component.GetString(SpeakingTo);
        var sourceText = Component.GetString(SourceText);
        var contextNotes = Component.GetString(ContextNotes);
        var direction = Component.GetString(Direction);

        // Display most important properties in the summary
        var summaryText = $"{characterId}: {sourceText}";

        // Display important properties in the tooltip
        var sb = new StringBuilder();

        sb.AppendLine(sourceText);
        sb.AppendLine();

        sb.AppendLine($"Character: {characterId}");
        if (!string.IsNullOrEmpty(speakingTo))
        {
            sb.AppendLine($"Speaking To: {speakingTo}");
        }
        if (!string.IsNullOrEmpty(contextNotes))
        {
            sb.AppendLine($"Context Notes: {contextNotes}");
        }
        if (!string.IsNullOrEmpty(direction))
        {
            sb.AppendLine($"Direction: {direction}");
        }

        sb.AppendLine();
        sb.AppendLine($"{dialogueKey}"); // Dialogue key contains character id

        var tooltipText = sb.ToString();

        return new ComponentSummary(summaryText, tooltipText);
    }

    protected override Result<string> TryGetProperty(string propertyPath)
    {
        // Get list of available characters to populate the Character combo box
        if (propertyPath == "/characterIds")
        {
            var getCharactersResult = GetCharacterIds();
            if (getCharactersResult.IsFailure)
            {
                return Result<string>.Fail($"Failed to get character ids")
                    .WithErrors(getCharactersResult);
            }
            var characterIdJson = getCharactersResult.Value;

            return Result<string>.Ok(characterIdJson);
        }

        return Result<string>.Fail();
    }

    private Result<string> GetCharacterIds()
    {
        // Get the scene component on this entity
        var sceneComponentKey = new ComponentKey(Component.Key.Resource, 0);
        var getComponentResult = _entityService.GetComponent(sceneComponentKey);
        if (getComponentResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to get scene component: '{sceneComponentKey}'")
                .WithErrors(getComponentResult);
        }
        var sceneComponent = getComponentResult.Value;

        // Check the component is a scene component
        if (sceneComponent.Schema.ComponentType != SceneEditor.ComponentType)
        {
            return Result<string>.Fail($"Primary component is not a Scene component");
        }

        // Get the dialogue file resource from the scene component
        ResourceKey excelFileResource = sceneComponent.GetString("/dialogueFile");
        if (string.IsNullOrEmpty(excelFileResource))
        {
            return Result<string>.Fail($"Failed to get dialogue file property");
        }

        var activityResult = _activityService.GetActivity(nameof(ScreenplayActivity));
        if (activityResult.IsFailure || 
            activityResult.Value is not ScreenplayActivity screenplayActivity)
        {
            return Result<string>.Fail($"Failed to get Screenplay activity");
        }

        var charactersResult = screenplayActivity.GetCharacters(Component.Key.Resource);
        if (charactersResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to get characters")
                .WithErrors(charactersResult);
        }
        var characters = charactersResult.Value;

        // Build a list of character ids
        var characterIds = new List<string>();
        foreach (var character in characters)
        {
            var characterId = character.CharacterId;
            characterIds.Add(characterId);
        }

        // Convert the character list to JSON so we can return it as a component property
        var characterIdsJson = JsonSerializer.Serialize(characterIds);

        return Result<string>.Ok(characterIdsJson);
    }
}

