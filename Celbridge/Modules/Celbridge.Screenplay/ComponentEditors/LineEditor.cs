using System.Text.Json;
using System.Text.Json.Nodes;
using Celbridge.Entities;
using Celbridge.Workspace;

namespace Celbridge.Screenplay.Components;

public class LineEditor : ComponentEditorBase
{
    private const string _configPath = "Celbridge.Screenplay.Assets.Components.LineComponent.json";
    private const string _formPath = "Celbridge.Screenplay.Assets.Forms.LineForm.json";

    public const string ComponentType = "Screenplay.Line";
    public const string CharacterId = "/characterId";
    public const string SourceText = "/sourceText";

    private readonly IEntityService _entityService;

    public LineEditor(IWorkspaceWrapper workspaceWrapper)
    {
        _entityService = workspaceWrapper.WorkspaceService.EntityService;
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
        var sourceText = Component.GetString(SourceText);

        var summaryText = $"{characterId}: {sourceText}";
        return new ComponentSummary(summaryText, summaryText);
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
        var excelFileResource = sceneComponent.GetString("/dialogueFile");
        if (string.IsNullOrEmpty(excelFileResource))
        {
            return Result<string>.Fail($"Failed to get dialogue file property");
        }
        
        // Get the ScreenplayData component on the Excel resource
        var getScreenplayDataResult = _entityService.GetComponentOfType(excelFileResource, ScreenplayDataEditor.ComponentType);
        if (getScreenplayDataResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to get the ScreenplayData component from the Excel file resource");
        }
        var screenplayDataComponent = getScreenplayDataResult.Value;

        // Get the 'characters' property from the ScreenplayData component
        var getCharactersResult = screenplayDataComponent.GetProperty("/characters");
        if (getCharactersResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to get characters property");
        }
        var charactersJson = getCharactersResult.Value;

        // Parse the characters JSON and build a list of character ids
        var charactersObject = JsonNode.Parse(charactersJson) as JsonObject;
        if (charactersObject is null)
        {
            return Result<string>.Fail("Failed to parse characters JSON");
        }

        var characterIds = new List<string>();
        foreach (var kv in charactersObject)
        {
            var characterId = kv.Key;
            characterIds.Add(characterId);
        }

        // Convert the character list to JSON so we can return it as a component property

        var characterIdsJson = JsonSerializer.Serialize(characterIds);

        return Result<String>.Ok(characterIdsJson);
    }
}

