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
    public const string LineType = "/lineType";
    public const string LineId = "/lineId";
    public const string CharacterId = "/characterId";
    public const string SpeakingTo = "/speakingTo";
    public const string SourceText = "/sourceText";
    public const string ContextNotes = "/contextNotes";
    public const string Direction = "/direction";
    public const string GameArea = "/gameArea";
    public const string TimeConstraint = "/timeConstraint";
    public const string SoundProcessing = "/soundProcessing";
    public const string Platform = "/platform";
    public const string LinePriority = "/linePriority";
    public const string ProductionStatus = "/productionStatus";

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

        var getKeyResult = GetDialogueKey();
        if (getKeyResult.IsSuccess)
        {
            var dialogueKey = getKeyResult.Value;

            sb.AppendLine();
            sb.AppendLine($"{dialogueKey}"); 
        }

        var tooltipText = sb.ToString();

        return new ComponentSummary(summaryText, tooltipText);
    }

    protected override Result<string> TryGetProperty(string propertyPath)
    {
        // Get list of available characters to populate the Character combo box
        if (propertyPath == "/characterIds")
        {
            var getCharactersResult = GetFilteredCharacterIds();
            if (getCharactersResult.IsFailure)
            {
                return Result<string>.Fail($"Failed to get character ids")
                    .WithErrors(getCharactersResult);
            }
            var characterIds = getCharactersResult.Value;

            // Convert the character id list to JSON so we can return it as a component property
            var charactersJson = JsonSerializer.Serialize(characterIds);

            return Result<string>.Ok(charactersJson);
        }
        else if (propertyPath == "/characterIdVisibility")
        {
            // Determine visiblity for Character Id field
            var lineType = Component.GetString(LineEditor.LineType);
            switch (lineType)
            {
                case "Player":
                    return Result<string>.Ok(JsonSerializer.Serialize(Visibility.Collapsed));
                case "PlayerVariant":
                    return Result<string>.Ok(JsonSerializer.Serialize(Visibility.Visible));
                case "NPC":
                    return Result<string>.Ok(JsonSerializer.Serialize(Visibility.Visible));
                case "SceneNote":
                    return Result<string>.Ok(JsonSerializer.Serialize(Visibility.Collapsed));
            }
        }
        else if (propertyPath == "/variantVisibility")
        {
            // Determine visiblity for several different fields
            var lineType = Component.GetString(LineEditor.LineType);
            switch (lineType)
            {
                case "Player":
                    return Result<string>.Ok(JsonSerializer.Serialize(Visibility.Visible));
                case "PlayerVariant":
                    return Result<string>.Ok(JsonSerializer.Serialize(Visibility.Collapsed));
                case "NPC":
                    return Result<string>.Ok(JsonSerializer.Serialize(Visibility.Visible));
                case "SceneNote":
                    return Result<string>.Ok(JsonSerializer.Serialize(Visibility.Collapsed));
            }
        }
        else if (propertyPath == "/directionVisibility")
        {
            // Determine visibility for Direction field
            var lineType = Component.GetString(LineEditor.LineType);
            switch (lineType)
            {
                case "Player":
                    return Result<string>.Ok(JsonSerializer.Serialize(Visibility.Visible));
                case "PlayerVariant":
                    return Result<string>.Ok(JsonSerializer.Serialize(Visibility.Visible));
                case "NPC":
                    return Result<string>.Ok(JsonSerializer.Serialize(Visibility.Visible));
                case "SceneNote":
                    return Result<string>.Ok(JsonSerializer.Serialize(Visibility.Collapsed));
            }
        }
        else if (propertyPath == "/directionPlaceholderText")
        {
            return GetDirectionPlaceholderText();
        }
        else if (propertyPath == "/generateDialogueKeyVisibility")
        {
            if (Component.GetString(LineType) == "PlayerVariant")
            {
                return Result<string>.Ok(JsonSerializer.Serialize(Visibility.Collapsed));
            }
            else
            {
                return Result<string>.Ok(JsonSerializer.Serialize(Visibility.Visible));
            }
        }
        else if (propertyPath == "/lineId")
        {
            if (Component.GetString(LineType) == "PlayerVariant")
            {
                // Get the Player line that's the parent of this Player Variant line
                var getParentResult = GetParentPlayerLine();
                if (getParentResult.IsSuccess)
                {
                    // Return the parent's Line Id
                    var playerLine = getParentResult.Value;
                    var lineId = playerLine.GetString(LineId);
                    if (!string.IsNullOrEmpty(lineId))
                    {
                        return Result<string>.Ok(JsonSerializer.Serialize(lineId));
                    }
                }
            }
        }
        else if (propertyPath == "/dialogueKey")
        {
            var getKeyResult = GetDialogueKey();
            if (getKeyResult.IsSuccess)
            {
                var dialogueKey = getKeyResult.Value;
                return Result<string>.Ok(JsonSerializer.Serialize(dialogueKey));
            }
        }

        // The property was not overridden
        return Result<string>.Fail();
    }

    public override void OnButtonClicked(string buttonId)
    {
        if (buttonId == "GenerateDialogueKey")
        {
            GenerateDialogueKey();
        }
    }

    protected override void OnFormPropertyChanged(string propertyPath)
    { 
        if (propertyPath == "/lineType")
        {
            // Update virtual properties when the line type changes
            // The character ids list will be filtered depending on the line type.
            NotifyFormPropertyChanged("/characterIds");
            NotifyFormPropertyChanged("/characterId");
            NotifyFormPropertyChanged("/characterIdVisibility");
            NotifyFormPropertyChanged("/variantVisibility");
            NotifyFormPropertyChanged("/directionVisibility");
            NotifyFormPropertyChanged("/dialogueKey");
            NotifyFormPropertyChanged("/generateDialogueKeyVisibility");

            // Get the filtered list of character ids            
            var getResult = GetProperty("/characterIds");
            if (getResult.IsFailure)
            {
                return;
            }
            var characterIdsJson = getResult.Value;
            var characterIds = JsonSerializer.Deserialize<List<string>>(characterIdsJson) ?? new List<string>();

            // Get the currently selected character id
            var characterId = Component.GetString(CharacterId);
            if (string.IsNullOrEmpty(characterId) ||
                !characterIds.Contains(characterId))
            {
                // If the current character id is not in the filtered list, default to the first character id in the list
                if (characterIds.Count > 0)
                {
                    var characterIdJson = JsonSerializer.Serialize(characterIds[0]);
                    Component.SetProperty(CharacterId, characterIdJson);
                }
                else
                {
                    Component.SetProperty(CharacterId, JsonSerializer.Serialize(string.Empty));
                }
            }
        }
        else if (propertyPath == "/lineId" || propertyPath == "/characterId")
        {
            NotifyFormPropertyChanged("/dialogueKey");
        }
    }

    private Result<List<string>> GetFilteredCharacterIds()
    {
        var characterIds = new List<string>();

        var lineType = Component.GetString(LineEditor.LineType);

        // Filter the character list based on the line type
        if (lineType == "NPC" || lineType == "PlayerVariant")
        {
            var getCharactersResult = GetAllCharacters();
            if (getCharactersResult.IsFailure)
            {
                return Result<List<string>>.Fail($"Failed to get characters")
                    .WithErrors(getCharactersResult);
            }
            var characters = getCharactersResult.Value;

            // Build a list of character ids
            foreach (var character in characters)
            {
                if (lineType == "PlayerVariant" && !character.Tag.StartsWith("Character.Player."))
                {
                    continue;
                }

                if (lineType == "NPC" && character.Tag.StartsWith("Character.Player"))
                {
                    continue;
                }

                var characterId = character.CharacterId;
                characterIds.Add(characterId);
            }
        }
        else if (lineType == "Player")
        {
            // Single character id in list: "Player"
            characterIds.Add("Player");
        }
        else if (lineType == "SceneNote")
        {
            // Single character id in list: "SceneNote"
            characterIds.Add("SceneNote");
        }

        return Result<List<string>>.Ok(characterIds);
    }

    private Result<List<Character>> GetAllCharacters()
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
        if (!sceneComponent.IsComponentType(SceneEditor.ComponentType))
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

    /// <summary>
    /// Construct a Dialogue Key based on the character id, namespace and line id.
    /// </summary>
    private Result<string> GetDialogueKey()
    {
        //
        // Get all components 
        //

        var sceneResource = Component.Key.Resource;
        var getComponentsResult = _entityService.GetComponents(sceneResource);
        if (getComponentsResult.IsFailure)
        {
            return Result<string>.Fail();
        }

        var components = getComponentsResult.Value;
        if (components.Count == 0)
        {
            return Result<string>.Fail();
        }

        //
        // Get the namespace from the Scene component on this entity
        //

        if (!components[0].IsComponentType(SceneEditor.ComponentType))
        {
            _logger.LogError($"Failed to update dialogue key. First component is not a Scene component.");
            return Result<string>.Fail();
        }

        var @namespace = components[0].GetString(SceneEditor.Namespace);
        if (string.IsNullOrEmpty(@namespace))
        {
            _logger.LogError($"Failed to update dialogue key. Namespace is empty.");
            return Result<string>.Fail();
        }

        //
        // Get the Character Id
        //

        var characterId = Component.GetString(CharacterId);

        var getLineIdResult = GetProperty(LineId); // Use the property override mechanism
        if (getLineIdResult.IsFailure)
        {
            return Result<string>.Fail();
        }
        var lineId = JsonSerializer.Deserialize<string>(getLineIdResult.Value);

        // Todo: Add a validation util for dialogue keys
        var dialogueKey = $"{characterId}-{@namespace}-{lineId}";

        return Result<string>.Ok(dialogueKey);
    }

    private void GenerateDialogueKey()
    {
        // Generate a new dialogue key by assigning a new unique line id.

        var getLineIdResult = GetUniqueLineId();
        if (getLineIdResult.IsFailure)
        {
            // Todo: Show an alert if generating line id fails
            _logger.LogError($"Failed to get a unique line id. {getLineIdResult.Error}");
            return;
        }
        var lineId = getLineIdResult.Value;

        Component.SetProperty(LineId, JsonSerializer.Serialize(lineId));
    }

    private Result<string> GetUniqueLineId()
    {
        //
        // Get all components on this entity
        //

        var sceneResource = Component.Key.Resource;
        var getComponentsResult = _entityService.GetComponents(sceneResource);
        if (getComponentsResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to get components for resource '{sceneResource}'")
                .WithErrors(getComponentsResult);
        }
        var components = getComponentsResult.Value;
        if (components.Count == 0)
        {
            return Result<string>.Fail($"Failed to get components for resource '{sceneResource}'");
        }

        //
        // Find all currently used line ids in this scene
        //

        var activeLineIds = new HashSet<string>();
        for (int i = 0; i < components.Count; i++)
        {
            var lineComponent = components[i];
            if (!lineComponent.IsComponentType(ComponentType))
            {
                // Skip non-line components
                continue;
            }

            // Get the line id property
            var lineId = lineComponent.GetString(LineId);
            if (string.IsNullOrEmpty(lineId))
            {
                // Skip empty dialogue keys
                continue;
            }

            if (!string.IsNullOrEmpty(lineId))
            {
                activeLineIds.Add(lineId);
            }
        }

        //
        // Generate a new unique line id
        //

        var newLineId = string.Empty;
        var random = new Random();
        do
        {
            // Try a random 4 digit hex code until a unique one is found.
            int number = random.Next(0x1000, 0x10000);
            newLineId = number.ToString("X4");
        }
        while (activeLineIds.Contains(newLineId));

        return Result<string>.Ok(newLineId);
    }

    /// <summary>
    /// Returns the placeholder text to display in the Direction text field.
    /// This applies when the Direction property of a Player Variant line is empty.
    /// In this case, the Direction property of the parent Player Line is displayed as placeholder text.
    /// In all other cases the placeholder text should be empty.
    /// </summary>
    private Result<string> GetDirectionPlaceholderText()
    {
        var lineType = Component.GetString(LineEditor.LineType);
        if (lineType != "PlayerVariant")
        {
            // Line is not a Player Variant
            return Result<string>.Ok(JsonSerializer.Serialize(string.Empty));
        }

        var getParentResult = GetParentPlayerLine();
        if (getParentResult.IsSuccess)
        {
            var parentLine = getParentResult.Value;

            // Found the Player Line, return the direction property.
            var otherDirection = parentLine.GetString("/direction");
            return Result<string>.Ok(JsonSerializer.Serialize(otherDirection));
        }

        return Result<string>.Ok(JsonSerializer.Serialize(string.Empty));
    }

    private Result<IComponentProxy> GetParentPlayerLine()
    {
        var lineType = Component.GetString(LineEditor.LineType);
        if (lineType != "PlayerVariant")
        {
            // This Line is not a Player Variant
            return Result<IComponentProxy>.Fail();
        }

        // Search through preceding lines for the parent Player Line
        int index = Component.Key.ComponentIndex - 1;
        while (index > 0)
        {
            var lineComponentKey = Component.Key with
            {
                ComponentIndex = index
            };
            index--;

            var getComponentResult = _entityService.GetComponent(lineComponentKey);
            if (getComponentResult.IsSuccess)
            {
                // Ignore other component types, e.g. Empty components.
                var otherLineComponent = getComponentResult.Value;
                if (otherLineComponent.IsComponentType(LineEditor.ComponentType))
                {
                    var otherLineType = otherLineComponent.GetString("/lineType");
                    if (otherLineType == "PlayerVariant")
                    {
                        // Skip over other Player Variant lines
                        continue;
                    }
                    else if (otherLineType == "Player")
                    {
                        return Result<IComponentProxy>.Ok(otherLineComponent);
                    }
                    else
                    {
                        // Any other line type means we're in an error state, give up.
                        break;
                    }
                }
            }
        }

        return Result<IComponentProxy>.Fail();
    }
}

