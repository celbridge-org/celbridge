using Celbridge.Documents.ViewModels;
using Celbridge.Entities;
using Celbridge.Screenplay.Components;
using Celbridge.Screenplay.Models;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using Humanizer;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Celbridge.Screenplay.ViewModels;

public partial class SceneDocumentViewModel : DocumentViewModel
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private IEntityService EntityService => _workspaceWrapper.WorkspaceService.EntityService;

    [ObservableProperty]
    private string _htmlContent = string.Empty;

    // Code gen requires a parameterless constructor
    public SceneDocumentViewModel()
    {
        throw new NotImplementedException();
    }

    public SceneDocumentViewModel(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public Result LoadContent()
    {
        var generateResult = GenerateScreenplayContent(FileResource);
        if (generateResult.IsFailure)
        {
            return Result.Fail($"Failed to generate screenplay content")
                .WithErrors(generateResult);
        }

        HtmlContent = generateResult.Value;
        return Result.Ok();
    }

    private Result<string> GenerateScreenplayContent(ResourceKey sceneResource)
    {
        var getComponentsResult = EntityService.GetComponents(sceneResource);
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

        var getCharactersResult = GetCharacters(sceneResource);
        if (getCharactersResult.IsFailure)
        {
            return Result<string>.Fail("Failed to get Character list")
                .WithErrors(getCharactersResult);
        }
        var characters = getCharactersResult.Value;

        var ns = sceneComponent.GetString(SceneEditor.Namespace);
        ns = ns.Humanize(LetterCasing.Title);
        var namespaceText = WebUtility.HtmlEncode(ns);

        var contextText = WebUtility.HtmlEncode(sceneComponent.GetString(SceneEditor.Context));

        var sb = new StringBuilder();

        // Generate only the body content (HTML structure is in index.html)
        sb.AppendLine("<div class=\"screenplay\">");
        sb.AppendLine("<div class=\"page\">");

        sb.AppendLine($"<div class=\"scene\">{namespaceText}</div>");
        sb.AppendLine($"<div class=\"scene-note\">{contextText}</div>");

        string playerDirection = string.Empty;

        foreach (var component in components)
        {
            if (component.IsComponentType(LineEditor.ComponentType))
            {
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
                    playerDirection = direction;
                }
                else if (!isPlayerVariant)
                {
                    playerDirection = string.Empty;
                }

                if (isPlayerVariant && string.IsNullOrEmpty(direction))
                {
                    direction = playerDirection;
                }

                var directionText = WebUtility.HtmlEncode(direction);

                if (characterId == "SceneNote")
                {
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

        return Result<string>.Ok(sb.ToString());
    }

    private Result<List<Character>> GetCharacters(ResourceKey sceneResource)
    {
        var sceneComponentKey = new ComponentKey(sceneResource, 0);
        var getComponentResult = EntityService.GetComponent(sceneComponentKey);
        if (getComponentResult.IsFailure)
        {
            return Result<List<Character>>.Fail($"Failed to get scene component: '{sceneComponentKey}'")
                .WithErrors(getComponentResult);
        }
        var sceneComponent = getComponentResult.Value;

        if (!sceneComponent.IsComponentType(SceneEditor.ComponentType))
        {
            return Result<List<Character>>.Fail($"Root component of resource '{sceneResource}' is not a scene component");
        }

        var dialogueFileResource = sceneComponent.GetString("/dialogueFile");
        if (string.IsNullOrEmpty(dialogueFileResource))
        {
            return Result<List<Character>>.Fail($"Failed to get dialogue file property");
        }

        var getScreenplayDataResult = EntityService.GetComponentOfType(dialogueFileResource, ScreenplayDataEditor.ComponentType);
        if (getScreenplayDataResult.IsFailure)
        {
            return Result<List<Character>>.Fail($"Failed to get the ScreenplayData component from the Excel file resource");
        }
        var screenplayDataComponent = getScreenplayDataResult.Value;

        var getCharactersResult = screenplayDataComponent.GetProperty("/characters");
        if (getCharactersResult.IsFailure)
        {
            return Result<List<Character>>.Fail($"Failed to get characters property");
        }
        var charactersJson = getCharactersResult.Value;

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

            CharacterType characterType;
            var characterTag = string.Empty;
            if (characterName == "Player")
            {
                characterTag = "Character.Player";
                characterType = CharacterType.Player;
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

                if (characterTag.StartsWith("Character.Player."))
                {
                    characterType = CharacterType.PlayerVariant;
                }
                else if (characterTag.StartsWith("Character."))
                {
                    characterType = CharacterType.NPC;
                }
                else
                {
                    continue;
                }
            }

            var character = new Character(characterId, characterName, characterTag, characterType);
            characters.Add(character);
        }

        return Result<List<Character>>.Ok(characters);
    }
}
