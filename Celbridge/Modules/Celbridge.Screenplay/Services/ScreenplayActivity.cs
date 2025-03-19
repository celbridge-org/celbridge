using Celbridge.Activities;
using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Entities;
using Celbridge.Logging;
using Celbridge.Screenplay.Components;
using Celbridge.Screenplay.Models;
using Celbridge.Workspace;
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
    private readonly IDocumentsService _documentService;

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
        var activeLineIds = new HashSet<string>();
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

            // Build a list of the existing line ids so that we can allocate a new random line id that won't clash
            // with any existing one.
            var dialogueKey = component.GetString("/dialogueKey");
            var segments = dialogueKey.Split('-');
            if (segments.Length == 3)
            {
                var lineId = segments[2];
                activeLineIds.Add(lineId);
            }

            lineComponents[i] = component;
        }

        //
        // Second pass checks all line components are valid, and checks that
        // each line has a valid character id, line id and dialogue key.
        // If the current line id is invalid then a new one is generated.
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
            var lineId = string.Empty;
            if (segments.Length == 3)
            {
                lineId = segments[2];
            }

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

                    // Force the line id to match the player line id of the group.
                    // This happens for example when a player variant line is moved to a different group.
                    if (lineId != playerLineId)
                    {
                        correctLineId = playerLineId;
                    }
                }
            }
            else
            {
                // This is an NPC line, so stop tracking the player line group
                playerLineId = string.Empty;
            }

            if (string.IsNullOrEmpty(correctLineId))
            {
                // No line id has been assigned yet, assign a new random one
                var random = new Random();
                do
                {
                    // Try a random 4 digit hex code until a unique one is found.
                    int number = random.Next(0x1000, 0x10000);
                    correctLineId = number.ToString("X4");
                }
                while (activeLineIds.Contains(correctLineId));
                
                activeLineIds.Add(correctLineId);
            }
             
            // Ensure that a valid dialogue key is always assigned.
            var correctDialogueKey = $"{characterId}-{@namespace}-{correctLineId}";
            if (dialogueKey != correctDialogueKey)
            {
                // This operation currently can't be undone because the undo triggers the activity update. 
                // Todo: Fix the undo / redo logic to support this case.
                component.SetString("/dialogueKey", correctDialogueKey);
                dialogueKey = correctDialogueKey;
            }

            if (activeDialogueKeys.Contains(dialogueKey))
            {
                var error = new ComponentError(
                    ComponentErrorSeverity.Critical,
                    "Duplicate dialogue key",
                    "Dialogue keys must be unique for each line");
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

        var generateResult = GenerateScreenplayMarkdown(resource);
        if (generateResult.IsFailure)
        {
            return Result.Fail($"Failed to generate screenplay markdown").
                WithErrors(generateResult);
        }

        var markdown = generateResult.Value;

        // Set the contents of the document to the generated markdown
        var setContentResult = await _documentService.SetTextDocumentContentAsync(resource, markdown);
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

    private Result<string> GenerateScreenplayMarkdown(ResourceKey resource)
    {
        var getComponentResult = _entityService.GetComponentOfType(resource, SceneEditor.ComponentType);
        if (getComponentResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to get Scene component")
                .WithErrors(getComponentResult);
        }
        var sceneComponent = getComponentResult.Value;

        var categoryText = sceneComponent.GetString(SceneEditor.Category);
        var namespaceText = sceneComponent.GetString(SceneEditor.Namespace);

        var sb = new StringBuilder();

        sb.AppendLine($"# {categoryText}");
        sb.AppendLine();
        sb.AppendLine($"{namespaceText}");
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
            var character = lineComponent.GetString(LineEditor.CharacterId);
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

    // Todo: Make this into a utility or static method
    private Result<List<Character>> GetCharacters(ResourceKey SceneResource)
    {
        // Get the scene component on this entity
        var sceneComponentKey = new ComponentKey(SceneResource, 0);
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
            return Result<List<Character>>.Fail($"Primary component of resource '{SceneResource}' is not a scene component");
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
}
