using Celbridge.Activities;
using Celbridge.Entities;
using Celbridge.Logging;
using Celbridge.Screenplay.Models;
using Celbridge.Screenplay.Services;
using Celbridge.Workspace;
using System.Text.Json;
using System.Text;

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

    private readonly ILogger<LineEditor> _logger;
    private readonly IEntityService _entityService;
    private readonly IActivityService _activityService;

    public LineEditor(
        ILogger<LineEditor> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
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
            var getCharactersResult = GetCharactersAsJSON();
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

    public override void OnButtonClicked(string buttonId)
    {
        if (buttonId == "UpdateDialogueKey")
        {
            UpdateDialogueKey();
        }
    }

    private Result<string> GetCharactersAsJSON()
    {
        // Get the list of characters
        var getCharactersResult = GetCharacters();
        if (getCharactersResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to get characters")
                .WithErrors(getCharactersResult);
        }
        var characters = getCharactersResult.Value;

        // Build a list of character ids
        var characterIds = new List<string>();
        foreach (var character in characters)
        {
            var characterId = character.CharacterId;
            characterIds.Add(characterId);
        }

        // Convert the character id list to JSON so we can return it as a component property
        var characterIdsJson = JsonSerializer.Serialize(characterIds);

        return Result<string>.Ok(characterIdsJson);
    }

    private Result<List<Character>> GetCharacters()
    {
        // Get the scene component on this entity
        var sceneComponentKey = new ComponentKey(Component.Key.Resource, 0);
        var getComponentResult = _entityService.GetComponent(sceneComponentKey);
        if (getComponentResult.IsFailure)
        {
            return Result<List<Character>>.Fail($"Failed to get scene component: '{sceneComponentKey}'")
                .WithErrors(getComponentResult);
        }
        var sceneComponent = getComponentResult.Value;
        if (sceneComponent.Schema.ComponentType != SceneEditor.ComponentType)
        {
            return Result<List<Character>>.Fail($"Root component is not a Scene component");
        }

        // Get the dialogue file resource from the scene component
        ResourceKey excelFileResource = sceneComponent.GetString("/dialogueFile");
        if (string.IsNullOrEmpty(excelFileResource))
        {
            return Result<List<Character>>.Fail($"Failed to get dialogue file property");
        }

        var activityResult = _activityService.GetActivity(nameof(ScreenplayActivity));
        if (activityResult.IsFailure ||
            activityResult.Value is not ScreenplayActivity screenplayActivity)
        {
            return Result<List<Character>>.Fail($"Failed to get Screenplay activity");
        }

        var charactersResult = screenplayActivity.GetCharacters(Component.Key.Resource);
        if (charactersResult.IsFailure)
        {
            return Result<List<Character>>.Fail($"Failed to get characters")
                .WithErrors(charactersResult);
        }
        var characters = charactersResult.Value;

        return Result<List<Character>>.Ok(characters);
    }

    private void UpdateDialogueKey()
    {
        //
        // Get all components on this entity
        //

        var sceneResource = Component.Key.Resource;
        var getComponentsResult = _entityService.GetComponents(sceneResource);
        if (getComponentsResult.IsFailure)
        {
            _logger.LogError($"Failed to update dialogue key. {getComponentsResult.Error}");
            return;
        }
        var components = getComponentsResult.Value;
        if (components.Count == 0)
        {
            return;
        }

        //
        // Get the list of characters from the screenplay
        //

        var getCharactersResult = GetCharacters();
        if (getCharactersResult.IsFailure)
        {
            _logger.LogError($"Failed to update dialogue key. {getCharactersResult.Error}");
            return;
        }
        var characters = getCharactersResult.Value;

        //
        // Get the character speaking this line
        //

        var characterId = Component.GetString(CharacterId);
        if (string.IsNullOrEmpty(characterId))
        {
            _logger.LogError($"Failed to update dialogue key. Character id is empty.");
            return;
        }
        var speakingCharacter = characters.FirstOrDefault(c => c.CharacterId == characterId);
        if (speakingCharacter is null)
        {
            _logger.LogError($"Failed to update dialogue key. No character matching '{characterId}' was found.");
            return;
        }

        // Note if the current line is a player variant line
        bool isPlayerVariant = speakingCharacter.Tag.StartsWith("Character.Player.");

        //
        // Get the namespace from the Scene component on this entity
        //

        if (components[0].Schema.ComponentType != SceneEditor.ComponentType)
        {
            _logger.LogError($"Failed to update dialogue key. First component is not a Scene component.");
            return;
        }

        var @namespace = components[0].GetString(SceneEditor.Namespace);
        if (string.IsNullOrEmpty(@namespace))
        {
            _logger.LogError($"Failed to update dialogue key. Namespace is empty.");
            return;
        }

        //
        // Assign a new line id
        //

        // Build the set of line ids currently in use
        var activeLineIds = new HashSet<string>();

        var lineComponentIndex = Component.Key.ComponentIndex;
        var playerLineId = string.Empty;

        for (int i = 0; i < components.Count; i++)
        {
            var lineComponent = components[i];
            if (lineComponent.Schema.ComponentType != ComponentType)
            {
                // Skip non-line components
                continue;
            }

            // Get the dialogue key property
            var dialogueKey = lineComponent.GetString(DialogueKey);
            if (string.IsNullOrEmpty(dialogueKey))
            {
                // Skip empty dialogue keys
                continue;
            }

            // Extract the line id from the dialogue key and add it to the active set
            var lineId = string.Empty;
            var segments = dialogueKey.Split('-');
            if (segments.Length == 3)
            {
                lineId = segments[2];
            }
            if (!string.IsNullOrEmpty(lineId))
            {
                activeLineIds.Add(lineId);
            }

            // If this is a player variant line, attempt to find a preceding player line id.
            if (isPlayerVariant &&
                i < lineComponentIndex)
            {
                if (lineComponent.GetString(CharacterId) == "Player")
                {
                    playerLineId = lineId;
                }
            }
        }

        // If this is a player variant line and a player line id was found, then use the player line id.
        string newLineId = string.Empty;
        if (isPlayerVariant)
        {
            newLineId = playerLineId;
        }

        // If no new line id has been assigned yet then assign a unique line id
        if (string.IsNullOrEmpty(newLineId))
        {
            // Find a new line id that is not already in use
            var random = new Random();
            do
            {
                // Try a random 4 digit hex code until a unique one is found.
                int number = random.Next(0x1000, 0x10000);
                newLineId = number.ToString("X4");
            }
            while (activeLineIds.Contains(newLineId));
        }

        // Set the new dialogue key
        var newDialogueKey = $"{characterId}-{@namespace}-{newLineId}";
        var jsonValue = JsonSerializer.Serialize(newDialogueKey);
        Component.SetProperty(DialogueKey, jsonValue);
    }
}

