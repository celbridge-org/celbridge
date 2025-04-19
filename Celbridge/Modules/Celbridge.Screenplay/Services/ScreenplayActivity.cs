using Celbridge.Activities;
using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Entities;
using Celbridge.Logging;
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
    private readonly ILogger<ScreenplayActivity> _logger;
    private readonly ICommandService _commandService;
    private readonly IEntityService _entityService;
    private readonly IDocumentsService _documentsService;

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
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;
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

        var sceneComponent = components[0];
        if (sceneComponent.Schema.ComponentType == SceneEditor.ComponentType)
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

        // Todo: Check that the namespace matches one defined in the Screenplay component
        var @namespace = sceneComponent.GetString(SceneEditor.Namespace);
        if (string.IsNullOrEmpty(@namespace))
        {
            var error = new ComponentError(
                ComponentErrorSeverity.Critical,
                "Invalid namespace",
                "The namespace must not be empty");
            entityAnnotation.AddError(0, error);
        }

        // Get the character list from the screenplay component
        var getCharactersResult = GetCharacters(entity);
        if (getCharactersResult.IsFailure)
        {
            return Result.Fail(entity, $"Failed to get characters from screenplay component")
                .WithErrors(getCharactersResult);
        }
        var characters = getCharactersResult.Value;

        //
        // Remaining components must all be Line or Empty
        //

        var lineComponents = new Dictionary<int, IComponentProxy>();
        var activeDialogueKeys = new HashSet<string>();

        //
        // First pass checks component types are valid and records all
        // line ids that are used in this namespace.
        //

        for (int i = 1; i < components.Count; i++)
        {
            var component = components[i];

            if (component.Schema.ComponentType == EntityConstants.EmptyComponentType)
            {
                // Skip empty components
                continue;
            }

            if (component.Schema.ComponentType != LineEditor.ComponentType)
            {
                var error = new ComponentError(
                    ComponentErrorSeverity.Critical,
                    "Invalid component type",
                    "This component must be a 'Line' component");
                entityAnnotation.AddError(i, error);

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
        foreach (var i in componentIndices)
        {
            var component = lineComponents[i];

            //
            // Get the character id
            //

            var characterId = component.GetString(LineEditor.CharacterId);
            if (string.IsNullOrEmpty(characterId))
            {
                var error = new ComponentError(
                    ComponentErrorSeverity.Critical,
                    "Invalid character id",
                    "The character id must not be empty");
                entityAnnotation.AddError(i, error);

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
            if (character is null)
            {
                var error = new ComponentError(
                    ComponentErrorSeverity.Critical,
                    "Invalid character id",
                    "A valid character must be selected");
                entityAnnotation.AddError(i, error);

                // There's not much more we can do until the user selects a character id
                continue;
            }

            //
            // Get the existing dialogue key and line id
            //

            var dialogueKey = component.GetString("/dialogueKey");
            var segments = dialogueKey.Split('-');
            if (segments.Length != 3)
            {
                var error = new ComponentError(
                    ComponentErrorSeverity.Critical,
                    "Invalid dialogue key",
                    "Dialogue keys must be non-empty and contain 3 segments.");
                entityAnnotation.AddError(i, error);

                // Can't do any more checks until the user assigns a valid dialogue key
                continue;
            }

            var lineId = segments[2];

            bool isPlayerVariantLine = false;
            var correctLineId = lineId;

            if (character.Tag == "Character.Player")
            {
                // Start of a new player line group
                playerLineId = lineId;
            }
            else if (character.Tag.StartsWith("Character.Player."))
            {
                // Player variants lines must be part of a player line group
                if (string.IsNullOrEmpty(playerLineId))
                {
                    var error = new ComponentError(
                        ComponentErrorSeverity.Critical,
                        "Invalid player variant line",
                        "Player variant lines must be part of a player line group");
                    entityAnnotation.AddError(i, error);
                }
                else
                {
                    // Flag this as a player variant line
                    isPlayerVariantLine = true;
                    correctLineId = playerLineId; // Variant lines must have the same line id as the player line
                }
            }
            else
            {
                // This is an NPC line, so stop tracking the player line group
                playerLineId = string.Empty;
            }
             
            // Ensure that a valid dialogue key is always assigned.
            var correctDialogueKey = $"{characterId}-{@namespace}-{correctLineId}";
            if (dialogueKey != correctDialogueKey)
            {
                var error = new ComponentError(
                    ComponentErrorSeverity.Critical,
                    "Invalid dialogue key",
                    "The dialogue key is not correctly formed. Update the dialogue key to assign a correct one.");
                entityAnnotation.AddError(i, error);
            }

            if (activeDialogueKeys.Contains(dialogueKey))
            {
                var error = new ComponentError(
                    ComponentErrorSeverity.Critical,
                    "Duplicate dialogue key",
                    "Dialogue keys must be unique for each line. Update the dialogue key to assign a new one.");
                entityAnnotation.AddError(i, error);
            }
            activeDialogueKeys.Add(dialogueKey);

            // Indent player variant lines
            if (isPlayerVariantLine)
            {
                entityAnnotation.SetIndent(i, 1);
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

    public async Task<Result> ImportScreenplayAsync(ResourceKey screenplayResource)
    {
        var importer = _serviceProvider.AcquireService<ScreenplayImporter>();

        var importResult = await importer.ImportScreenplayAsync(screenplayResource);
        if (importResult.IsFailure)
        {
            return Result.Fail($"Failed to import screenplay data from Excel")
                .WithErrors(importResult);
        }

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
        if (sceneComponent.Schema.ComponentType != SceneEditor.ComponentType)
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
            components[0].Schema.ComponentType != SceneEditor.ComponentType)
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

        foreach (var component in components)
        {
            if (component.Schema.ComponentType == LineEditor.ComponentType)
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

                var directionText = WebUtility.HtmlEncode(component.GetString(LineEditor.Direction));

                sb.AppendLine($"<div class=\"{lineClass}\">");
                sb.AppendLine($"  <span class=\"character {colorClass}\">{displayCharacter}</span>");
                if (!string.IsNullOrEmpty(directionText))
                {
                    sb.AppendLine($"  <span class=\"direction\">({directionText})</span>");
                }
                sb.AppendLine($"  <span class=\"dialogue\">{sourceText}</span>");
                sb.AppendLine("</div>");
            }
            else if (component.Schema.ComponentType == EntityConstants.EmptyComponentType)
            {
                // Add scene note to the screenplay
                var commentText = component.GetString("/comment");
                sb.AppendLine($"<div class=\"scene-note\">{commentText}</div>");
            }
        }

        sb.AppendLine("</div>"); // page
        sb.AppendLine("</div>"); // screenplay
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return Result<string>.Ok(sb.ToString());
    }
}
