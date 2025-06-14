using Celbridge.Activities;
using Celbridge.Dialog;
using Celbridge.Documents;
using Celbridge.Entities;
using Celbridge.Explorer;
using Celbridge.Localization;
using Celbridge.Screenplay.Components;
using Celbridge.Screenplay.Models;
using Celbridge.Workspace;
using Humanizer;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

using Path = System.IO.Path;

namespace Celbridge.Screenplay.Services;

public class ScreenplayActivity : IActivity
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILocalizerService _localizerService;
    private readonly IDialogService _dialogService;
    private readonly IEntityService _entityService;
    private readonly IDocumentsService _documentsService;
    private readonly IWorkspaceSettings _workspaceSettings;
    private readonly IResourceRegistry _resourceRegistry;

    public ScreenplayActivity(
        IServiceProvider serviceProvider,
        ILocalizerService localizerService,
        IDialogService dialogService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _serviceProvider = serviceProvider;
        _localizerService = localizerService;
        _dialogService = dialogService;
        _entityService = workspaceWrapper.WorkspaceService.EntityService;
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;
        _workspaceSettings = workspaceWrapper.WorkspaceService.WorkspaceSettings;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;
    }

    public async Task<Result> ActivateAsync()
    {
        // Register a HTML preview provider for .scene files
        var provider = _serviceProvider.AcquireService<IHTMLPreviewProvider>();

        var addProviderResult = _documentsService.AddPreviewProvider(".scene", provider);
        if (addProviderResult.IsFailure)
        {
            return Result.Fail("Failed to register HTML preview provider for '.scene' file extension.")
                .WithErrors(addProviderResult);
        }

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

    public Result AnnotateEntity(ResourceKey entity, IEntityAnnotation entityAnnotation)
    {
        //
        // These cases should never happen, so they are hard errors instead of annotation errors
        //

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
        // Root component must be "Scene"
        //

        var sceneComponent = components[0];
        if (sceneComponent.IsComponentType(SceneEditor.ComponentType))
        {
            entityAnnotation.SetIsRecognized(0);
        }
        else
        {
            entityAnnotation.AddComponentError(0, new AnnotationError(
                AnnotationErrorSeverity.Critical,
                "Invalid component position",
                "This component must be the first component."));
        }

        // Todo: Check that the namespace matches one defined in the Screenplay component
        var @namespace = sceneComponent.GetString(SceneEditor.Namespace);
        if (string.IsNullOrEmpty(@namespace))
        {
            entityAnnotation.AddComponentError(0, new AnnotationError(
                AnnotationErrorSeverity.Error,
                "Invalid namespace",
                "The namespace must not be empty"));
        }

        // Get the character list from the screenplay component
        var getCharactersResult = GetCharacters(entity);
        if (getCharactersResult.IsFailure)
        {
            entityAnnotation.AddEntityError(new AnnotationError(
                AnnotationErrorSeverity.Error,
                "Failed to get characters",
                "Failed to get character list from Screenplay component"));
        }
        var characters = getCharactersResult.Value;

        //
        // Remaining components must all be Line or Empty
        //

        var lineComponents = new Dictionary<int, IComponentProxy>();
        var activeLineIds = new HashSet<string>();
        var activePlayerVariants = new HashSet<string>();

        //
        // First pass checks component types are valid and records all
        // line ids that are used in this namespace.
        //

        for (int i = 1; i < components.Count; i++)
        {
            var component = components[i];

            if (component.IsComponentType(EntityConstants.EmptyComponentType))
            {
                // Skip empty components
                continue;
            }

            if (!component.IsComponentType(LineEditor.ComponentType))
            {
                entityAnnotation.AddComponentError(i, new AnnotationError(
                    AnnotationErrorSeverity.Error,
                    "Invalid component type",
                    "This component must be a 'Line' component"));

                continue;
            }

            // Mark Line component as recognized
            entityAnnotation.SetIsRecognized(i);

            lineComponents[i] = component;
        }

        //
        // Second pass checks all line components are valid, and checks that
        // each line has a valid character id, line id and dialogue key.
        //

        var componentIndices = lineComponents.Keys.ToList();
        componentIndices.Sort();

        var playerLineId = string.Empty;
        var playerSpeakingTo = string.Empty;

        foreach (var i in componentIndices)
        {
            var component = lineComponents[i];

            //
            // Get the line type
            //

            var lineType = component.GetString(LineEditor.LineType);
            if (lineType != "Player" &&
                lineType != "PlayerVariant" &&
                lineType != "NPC" &&
                lineType != "SceneNote")
            {
                entityAnnotation.AddComponentError(i, new AnnotationError(
                    AnnotationErrorSeverity.Error,
                    "Invalid line type",
                    "The line type must be Player, PlayerVariant, NPC or SceneNote"));

                continue;
            }


            //
            // Get the character id
            //

            var characterId = component.GetString(LineEditor.CharacterId);
            if (string.IsNullOrEmpty(characterId))
            {
                entityAnnotation.AddComponentError(i, new AnnotationError(
                    AnnotationErrorSeverity.Error,
                    "Invalid character id",
                    "The character id must not be empty"));

                continue;
            }

            Character? character = null;
            foreach (var c in characters)
            {
                if (characterId == c.CharacterId)
                {
                    character = c;
                    break;
                }
            }
            if (lineType != "SceneNote" &&
                character is null)
            {
                entityAnnotation.AddComponentError(i, new AnnotationError(
                    AnnotationErrorSeverity.Error,
                    "Invalid character id",
                    "A valid character must be selected"));

                // There's not much more we can do until the user selects a valid character id
                continue;
            }

            //
            // Get the line id
            //

            var lineId = component.GetString("/lineId");

            // PlayerVariants inherit their parent's Line Id, so their own LineId property may be empty.
            // All other line types must have a non-empty LineId
            if (lineType != "PlayerVariant" &&
                string.IsNullOrEmpty(lineId))
            {
                entityAnnotation.AddComponentError(i, new AnnotationError(
                    AnnotationErrorSeverity.Error,
                    "Invalid dialogue key",
                    "Line id must not be empty. Generate a new dialogue key to fix this."));

                // We can't perform any more checks until the user assigns a valid line id
                continue;
            }

            var speakingTo = component.GetString("/speakingTo");

            bool isPlayerVariantLine = false;

            // PlayerVariant lines inherit their Line Id from their parents.
            var resolvedLineId = lineId;

            if (lineType != "SceneNote")
            {
                Guard.IsNotNull(character);

                if (character.Tag == "Character.Player")
                {
                    // Start of a new Player line group
                    playerLineId = lineId;
                    playerSpeakingTo = speakingTo;
                }
                else if (character.Tag.StartsWith("Character.Player."))
                {
                    // Player Variant lines must be part of a player line group
                    if (string.IsNullOrEmpty(playerLineId))
                    {
                        entityAnnotation.AddComponentError(i, new AnnotationError(
                            AnnotationErrorSeverity.Error,
                            "Invalid variant line",
                            "Player Variant lines must be part of a Player Line group"));
                    }
                    else
                    {
                        // Flag this as a player variant line.
                        isPlayerVariantLine = true;

                        // Inherit the parent Player line's Line Id.
                        resolvedLineId = playerLineId;
                    }
                }
                else
                {
                    // This is an NPC line, so stop tracking the Player Line group
                    playerLineId = string.Empty;
                    playerSpeakingTo = string.Empty;
                }
            }

            //
            // Check the Line Id is valid and unique. 
            //

            if (lineType == "PlayerVariant")
            {
                // Each Player Variant within the group must specify a different character id.
                var dialogueKey = $"{characterId}-{@namespace}-{resolvedLineId}";
                if (activePlayerVariants.Contains(dialogueKey))
                {
                    entityAnnotation.AddComponentError(i, new AnnotationError(
                        AnnotationErrorSeverity.Error,
                        "Invalid character",
                        "This character is already selected in a Player Variant line."));
                }

                activePlayerVariants.Add(dialogueKey);
            }
            else
            {
                // Check that a valid line id is assigned.
                if (lineId != resolvedLineId)
                {
                    // Todo: Is this error mode still possible?
                    entityAnnotation.AddComponentError(i, new AnnotationError(
                        AnnotationErrorSeverity.Error,
                        "Invalid dialogue key",
                        "The line id is not valid. Generate a new dialogue key to fix this."));
                }

                // Check that each line id is unique.
                if (activeLineIds.Contains(lineId))
                {
                    entityAnnotation.AddComponentError(i, new AnnotationError(
                        AnnotationErrorSeverity.Error,
                        "Invalid dialogue key",
                        "Every line must have a unique line id. Generate a new dialogue key to fix this."));
                }
                else
                {
                    activeLineIds.Add(lineId);
                }
            }

            // Indent player variant lines
            if (isPlayerVariantLine)
            {
                entityAnnotation.SetIndent(i, 1);
            }
        }

        return Result.Ok();
    }

    public async Task<Result> UpdateResourceContentAsync(ResourceKey resource, IEntityAnnotation entityAnnotation)
    {
        var count = _entityService.GetComponentCount(resource);
        if (count == 0)
        {
            // Resource may have been deleted or moved since the update was requested
            return Result.Ok();
        }

        var generateResult = GenerateScreenplayHTML(resource);
        if (generateResult.IsFailure)
        {
            return Result.Fail($"Failed to generate screenplay markdown").
                WithErrors(generateResult);
        }

        var markdown = generateResult.Value;

        // Set the contents of the document to the generated markdown
        var setContentResult = await _documentsService.SetTextDocumentContentAsync(resource, markdown);
        if (setContentResult.IsFailure)
        {
            return Result.Fail($"Failed to set document content")
                .WithErrors(setContentResult);
        }

        await Task.CompletedTask;

        return Result.Ok();
    }

    public async Task<Result> LoadScreenplayAsync(ResourceKey screenplayResource)
    {
        // If the load will overwrite modified scenes, ask the user if it's ok to proceed.
        var confirmed = await ConfirmLoadScreenplay();
        if (!confirmed)
        {
            return Result.Ok();
        }

        // Display a progress dialog
        var dialogTitleText = _localizerService.GetString("Screenplay_LoadingScreenplayTitle");
        var progressToken = _dialogService.AcquireProgressDialog(dialogTitleText);

        // Give the progress dialog a chance to display
        await Task.Delay(100);

        var loader = _serviceProvider.AcquireService<ScreenplayLoader>();

        var loadResult = await loader.LoadScreenplayAsync(screenplayResource);
        _dialogService.ReleaseProgressDialog(progressToken);

        if (loadResult.IsFailure)
        {
            // Alert the user about the failed load
            var alertTitleText = _localizerService.GetString("Screenplay_LoadFailedTitle");
            var alertBodyText = _localizerService.GetString("Screenplay_LoadFailedMessage");
            await _dialogService.ShowAlertDialogAsync(alertTitleText, alertBodyText);

            return Result.Fail($"Failed to load screenplay data from Workbook")
                .WithErrors(loadResult);
        }

        // Reset list of modified scenes
        await _workspaceSettings.DeletePropertyAsync(ScreenplayConstants.ModifiedScenesKey);

        return Result.Ok();
    }

    public async Task<Result> SaveScreenplayAsync(ResourceKey screenplayResource)
    {
        // Display a progress dialog
        var dialogueTitleText = _localizerService.GetString("Screenplay_SavingScreenplayTitle");
        var progressToken = _dialogService.AcquireProgressDialog(dialogueTitleText);

        // Give the progress dialog a chance to display
        await Task.Delay(100);

        var saver = _serviceProvider.AcquireService<ScreenplaySaver>();

        var saveResult = saver.SaveScreenplay(screenplayResource);
        _dialogService.ReleaseProgressDialog(progressToken);

        if (saveResult.IsFailure)
        {
            // Alert the user about the failed save
            var alertTitleText = _localizerService.GetString("Screenplay_SaveFailedTitle");
            var alertBodyText = _localizerService.GetString("Screenplay_SaveFailedMessage");
            await _dialogService.ShowAlertDialogAsync(alertTitleText, alertBodyText);

            return Result.Fail($"Failed to save screenplay data to Workbook")
                .WithErrors(saveResult);
        }

        // All modified scenes have now been saved, so reset the modified scenes list
        await _workspaceSettings.DeletePropertyAsync(ScreenplayConstants.ModifiedScenesKey);

        return Result.Ok();
    }

    public Result<List<Character>> GetCharacters(ResourceKey sceneResource)
    {
        // Get the scene component on this entity
        var sceneComponentKey = new ComponentKey(sceneResource, 0);
        var getComponentResult = _entityService.GetComponent(sceneComponentKey);
        if (getComponentResult.IsFailure)
        {
            return Result<List<Character>>.Fail($"Failed to get scene component: '{sceneComponentKey}'")
                .WithErrors(getComponentResult);
        }
        var sceneComponent = getComponentResult.Value;

        // Check the component is a scene component
        if (!sceneComponent.IsComponentType(SceneEditor.ComponentType))
        {
            return Result<List<Character>>.Fail($"Root component of resource '{sceneResource}' is not a scene component");
        }

        // Get the dialogue file resource from the scene component
        var dialogueFileResource = sceneComponent.GetString("/dialogueFile");
        if (string.IsNullOrEmpty(dialogueFileResource))
        {
            return Result<List<Character>>.Fail($"Failed to get dialogue file property");
        }

        // Get the ScreenplayData component from the dialogue file resource
        var getScreenplayDataResult = _entityService.GetComponentOfType(dialogueFileResource, ScreenplayDataEditor.ComponentType);
        if (getScreenplayDataResult.IsFailure)
        {
            return Result<List<Character>>.Fail($"Failed to get the ScreenplayData component from the Excel file resource");
        }
        var screenplayDataComponent = getScreenplayDataResult.Value;

        // Get the 'characters' property from the ScreenplayData component
        var getCharactersResult = screenplayDataComponent.GetProperty("/characters");
        if (getCharactersResult.IsFailure)
        {
            return Result<List<Character>>.Fail($"Failed to get characters property");
        }
        var charactersJson = getCharactersResult.Value;

        // Parse the characters JSON and build a list of characters
        var charactersObject = JsonNode.Parse(charactersJson) as JsonObject;
        if (charactersObject is null)
        {
            return Result<List<Character>>.Fail("Failed to parse characters JSON");
        }

        var characters = new List<Character>();
        foreach (var kv in charactersObject)
        {
            var characterId = kv.Key;
            var characterProperties = kv.Value as JsonObject;

            if (characterProperties is null)
            {
                return Result<List<Character>>.Fail("Failed to parse character properties");
            }

            var characterName = string.Empty;
            if (characterProperties.TryGetPropertyValue("name", out JsonNode? nameValue) &&
                nameValue is not null)
            {
                characterName = nameValue.ToString() ?? string.Empty;
            }
            if (string.IsNullOrEmpty(characterName))
            {
                return Result<List<Character>>.Fail("Character name is empty");
            }

            var characterTag = string.Empty;
            if (characterName == "Player")
            {
                characterTag = "Character.Player";
            }
            else
            {
                if (characterProperties.TryGetPropertyValue("tag", out JsonNode? tagValue) &&
                    tagValue is not null)
                {
                    characterTag = tagValue.ToString() ?? string.Empty;
                }
                if (string.IsNullOrEmpty(characterTag))
                {
                    return Result<List<Character>>.Fail("Character tag is empty");
                }
            }

            var character = new Character(characterId, characterName, characterTag);
            characters.Add(character);
        }

        return Result<List<Character>>.Ok(characters);
    }

    private async Task<bool> ConfirmLoadScreenplay()
    {
        // Get the list of modified scenes from the workspace settings
        var modifiedScenes = await _workspaceSettings.GetPropertyAsync<HashSet<string>>(ScreenplayConstants.ModifiedScenesKey);
        if (modifiedScenes is null ||
            modifiedScenes.Count == 0)
        {
            return true;
        }

        // Construct a sorted list containing the filename of each scene 
        var sceneResources = new List<string>();
        foreach (var sceneResource in modifiedScenes)
        {
            var getResourceResult = _resourceRegistry.GetResource(sceneResource);
            if (getResourceResult.IsFailure)
            {
                // Ignore this scene resource because it no longer exists (e.g. user deleted it)
                continue;
            }

            var sceneFilename = Path.GetFileNameWithoutExtension(sceneResource);
            if (!string.IsNullOrEmpty(sceneFilename))
            {
                sceneResources.Add(sceneFilename);
            }
        }
        sceneResources.Sort();

        if (sceneResources.Count == 0)
        {
            // No existing scene resources will be affected by the load, so it can proceed.
            return true;
        }

        var maxScenes = 5;
        var sb = new StringBuilder();
        for (int i = 0; i < sceneResources.Count; i++)
        {
            var scene = sceneResources[i];
            if (i > maxScenes)
            {
                sb.Append($"...");
                break;
            }
            sb.AppendLine(scene.ToString());
        }
        var sceneListText = sb.ToString();

        // Ask the user to confirm that they want to overwrite the modified scenes
        var dialogTitleText = _localizerService.GetString("Screenplay_ConfirmLoadTitle");
        var dialogMessageText = _localizerService.GetString("Screenplay_ConfirmLoadMessage", sceneListText);

        var confirmResult = await _dialogService.ShowConfirmationDialogAsync(dialogTitleText, dialogMessageText);
        if (confirmResult.IsFailure)
        {
            return false;
        }
        var confirmed = confirmResult.Value;

        return confirmed;
    }

    private Result<string> GenerateScreenplayHTML(ResourceKey sceneResource)
    {
        // Get all components in the entity, including the Scene component

        var getComponentsResult = _entityService.GetComponents(sceneResource);
        if (getComponentsResult.IsFailure)
        {
            return Result<string>.Fail("Failed to get Line components")
                .WithErrors(getComponentsResult);
        }
        var components = getComponentsResult.Value;

        if (components.Count == 0 ||
            !components[0].IsComponentType(SceneEditor.ComponentType))
        {
            return Result<string>.Fail("Entity does not contain a Scene component");
        }

        var sceneComponent = components[0];

        // Get the list of characters

        var getCharactersResult = GetCharacters(sceneResource);
        if (getCharactersResult.IsFailure)
        {
            return Result<string>.Fail("Failed to get Character list")
                .WithErrors(getCharactersResult);
        }
        var characters = getCharactersResult.Value;

        // Construct the screenplay HTML

        var ns = sceneComponent.GetString(SceneEditor.Namespace);
        ns = ns.Humanize(LetterCasing.Title);
        var namespaceText = WebUtility.HtmlEncode(ns);

        var contextText = WebUtility.HtmlEncode(sceneComponent.GetString(SceneEditor.Context));

        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html>");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");

        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: Arial, sans-serif; margin: 0; padding: 0; background: transparent; }");
        sb.AppendLine(".screenplay { max-width: 800px; width: 100%; margin: 0 auto; }");
        sb.AppendLine(".page { max-width: 794px; margin: 0 auto; }");
        sb.AppendLine(".scene { text-align: left; margin-bottom: 2em; font-size: 2em; font-weight: bold; margin: 0 0 0.67em 0;}");
        sb.AppendLine(".scene-note { text-align: left; margin-bottom: 2em; font-style: italic; }");
        sb.AppendLine(".line { margin-bottom: 2em; text-align: center; }");
        sb.AppendLine(".character { display: block; font-weight: bold; text-transform: uppercase; margin-bottom: 0.5em; }");
        sb.AppendLine(".direction { text-align: center; }");
        sb.AppendLine(".dialogue { display: block; white-space: pre-wrap; }");
        sb.AppendLine(".variant { margin-right: 3em; margin-left: auto; text-align: right; font-style: italic; }");
        sb.AppendLine(".player-color { color: hsl(220, 80%, 60%); }");
        sb.AppendLine(".npc-color { color: hsl(10, 70%, 50%); }");
        sb.AppendLine("</style>");

        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div class=\"screenplay\">");
        sb.AppendLine("<div class=\"page\">");

        sb.AppendLine($"<div class=\"scene\">{namespaceText}</div>");
        sb.AppendLine($"<div class=\"scene-note\">{contextText}</div>");

        // If the Direction property of a Player Variant line is empty, it defaults to the 
        // Direction of its parent Player Line. We store the Player Line direction when we process it so
        // that we can output it for Player Variant Lines if needed.
        string playerDirection = string.Empty;

        foreach (var component in components)
        {
            if (component.IsComponentType(LineEditor.ComponentType))
            {
                // Add line to the screenplay

                var characterId = component.GetString(LineEditor.CharacterId);
                var sourceText = WebUtility.HtmlEncode(component.GetString(LineEditor.SourceText));
                if (string.IsNullOrWhiteSpace(characterId) || string.IsNullOrWhiteSpace(sourceText))
                {
                    continue;
                }

                string characterName = string.Empty;
                bool isPlayer = characterId == "Player";
                bool isPlayerVariant = false;

                foreach (var character in characters)
                {
                    if (character.CharacterId == characterId)
                    {
                        characterName = character.Name;
                        if (!isPlayer && character.Tag.StartsWith("Character.Player."))
                        {
                            isPlayerVariant = true;
                        }
                        break;
                    }
                }

                string displayCharacter = characterName == characterId
                    ? WebUtility.HtmlEncode($"{characterName}")
                    : WebUtility.HtmlEncode($"{characterName} ({characterId})");

                string lineClass = isPlayerVariant ? "line variant" : "line";
                string colorClass = isPlayer || isPlayerVariant ? "player-color" : "npc-color";

                var direction = component.GetString(LineEditor.Direction);

                if (isPlayer)
                {
                    // Record the Player direction to display as placeholder direction in PlayerVariant lines
                    playerDirection = direction;
                }
                else if (!isPlayerVariant)
                {
                    playerDirection = string.Empty;
                }

                // Display the recorded Player direction if the PlayerVariant direction is empty
                if (isPlayerVariant && string.IsNullOrEmpty(direction))
                {
                    direction = playerDirection;
                }

                var directionText = WebUtility.HtmlEncode(direction);

                if (characterId == "SceneNote")
                {
                    // Add scene note to the screenplay
                    sb.AppendLine($"<div class=\"scene-note\">{sourceText}</div>");
                }
                else
                {
                    sb.AppendLine($"<div class=\"{lineClass}\">");
                    sb.AppendLine($"  <span class=\"character {colorClass}\">{displayCharacter}</span>");
                    if (!string.IsNullOrEmpty(directionText))
                    {
                        sb.AppendLine($"  <span class=\"direction\">({directionText})</span>");
                    }
                    sb.AppendLine($"  <span class=\"dialogue\">{sourceText}</span>");
                    sb.AppendLine("</div>");
                }
            }
        }

        sb.AppendLine("</div>"); // page
        sb.AppendLine("</div>"); // screenplay
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return Result<string>.Ok(sb.ToString());
    }
}
