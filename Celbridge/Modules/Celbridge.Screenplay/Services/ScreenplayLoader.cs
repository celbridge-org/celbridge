using Celbridge.Entities;
using Celbridge.Explorer;
using Celbridge.Screenplay.Components;
using Celbridge.Screenplay.Models;
using Celbridge.Workspace;
using ClosedXML.Excel;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace Celbridge.Screenplay.Services;

public class ScreenplayLoader
{
    private IExplorerService _explorerService;
    private IWorkspaceWrapper _workspaceWrapper;

    public ScreenplayLoader(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
        _explorerService = workspaceWrapper.WorkspaceService.ExplorerService;
    }

    public async Task<Result> LoadScreenplayAsync(ResourceKey workbookFile)
    {
        try
        {
            var extension = Path.GetExtension(workbookFile);
            if (extension != ".xlsx")
            {
                return Result.Fail($"Unsupported file type: {extension}");
            }

            var entityService = _workspaceWrapper.WorkspaceService.EntityService;
            var resourceRegistry = _explorerService.ResourceRegistry;
            var workbookFilePath = resourceRegistry.GetResourcePath(workbookFile);
            var screenplayFolderPath = Path.GetFileNameWithoutExtension(workbookFilePath);

            // Acquire the ScreenplayData component from the workbook resource
            var getComponentResult = entityService.GetComponentOfType(workbookFile, ScreenplayDataEditor.ComponentType);
            if (getComponentResult.IsFailure)
            {
                return Result.Fail($"Failed to get ScreenplayData component from workbook file resource '{workbookFile}'")
                    .WithErrors(getComponentResult);
            }
            var screenplayData = getComponentResult.Value;

            // Open the workbook file.
            // It's best to do this before we make any other changes, e.g. in case the file is locked.
            using var workbook = new XLWorkbook(workbookFilePath);

            // Create a new screenplay folder for the screenplay
            if (Directory.Exists(screenplayFolderPath))
            {
                Directory.Delete(screenplayFolderPath, true);
            }
            Directory.CreateDirectory(screenplayFolderPath);

            // Update the resource registry to delete any existing entity data files before
            // we add the .scene files.
            var updateResult = resourceRegistry.UpdateResourceRegistry();
            if (updateResult.IsFailure)
            {
                return Result.Fail($"Failed to update resource registry")
                    .WithErrors(updateResult);
            }

            // Load the characters from the "Characters" worksheet
            var charactersWorksheet = workbook.Worksheet("Characters");
            var loadCharactersResult = LoadCharacters(charactersWorksheet);
            if (loadCharactersResult.IsFailure)
            {
                return Result.Fail($"Failed to load characters from 'Characters' worksheet")
                    .WithErrors(loadCharactersResult);
            }
            var characters = loadCharactersResult.Value;

            // Load the scenes from the "Scenes" worksheet                
            var scenesWorksheet = workbook.Worksheet("Scenes");
            var loadScenesResult = LoadScenes(scenesWorksheet);
            if (loadScenesResult.IsFailure)
            {
                return Result.Fail($"Failed to load scenes from 'Scenes' worksheet")
                    .WithErrors(loadScenesResult);
            }
            var scenes = loadScenesResult.Value;

            // Load the dialogue lines from the "Dialogue" worksheet
            var dialogueWorksheet = workbook.Worksheet("Dialogue");
            var loadLinesResult = LoadLines(dialogueWorksheet, characters);
            if (loadLinesResult.IsFailure)
            {
                return Result.Fail($"Failed to load dialogue lines from 'Dialogue' worksheet")
                    .WithErrors(loadLinesResult);
            }
            var lines = loadLinesResult.Value;

            // Validate dialogue data
            var validateResult = ValidateDialogue(scenes, characters, lines);
            if (validateResult.IsFailure)
            {
                return Result.Fail($"Failed to validate dialogue data")
                    .WithErrors(validateResult);
            }

            // Add the dialogue lines to each scene
            var addResult = AddSceneLines(scenes, lines);
            if (addResult.IsFailure)
            {
                return Result.Fail($"Failed to add lines to scenes")
                    .WithErrors(addResult);
            }

            // Save a .scene file for each scene
            var saveResult = await SaveSceneFilesAsync(scenes, workbookFile);
            if (saveResult.IsFailure)
            {
                return Result.Fail($"Failed to save .scene files")
                    .WithErrors(saveResult);
            }

            var populateResult = PopulateCharacters(screenplayData, characters);
            if (populateResult.IsFailure)
            {
                return Result.Fail($"Failed to populate characters property")
                    .WithErrors(populateResult);
            }

            return Result.Ok();
        }
        catch (IOException ex) when (ex.HResult == unchecked((int)0x80070020))
        {
            return Result.Fail("Excel file is in use by another process")
                .WithException(ex);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to load screenplay data from workbook")
                .WithException(ex);
        }
    }

    private Result<List<Character>> LoadCharacters(IXLWorksheet characterSheet)
    {
        var characters = new List<Character>();

        // Find the used range
        var range = characterSheet.RangeUsed();
        if (range == null)
        {
            return Result<List<Character>>.Fail("The sheet is empty.");
        }

        //
        // Read header row and map column names to indexes
        //

        var headerRow = range.FirstRowUsed();
        var columnMap = new Dictionary<string, int>();

        foreach (var cell in headerRow.CellsUsed())
        {
            var columnName = cell.GetValue<string>().Trim();
            if (!string.IsNullOrEmpty(columnName))
            {
                columnMap[columnName] = cell.Address.ColumnNumber;
            }
        }

        //
        // Validate required columns
        //

        string[] requiredColumns = { "CharacterId", "Name", "Tag" };
        foreach (var col in requiredColumns)
        {
            if (!columnMap.ContainsKey(col))
            {
                return Result<List<Character>>.Fail($"Missing required column '{col}'");
            }
        }

        //
        // Read data rows, skipping the header
        //

        foreach (var row in range.RowsUsed().Skip(1))
        {
            try
            {
                var tag = TryGetValue<string>(row, columnMap, nameof(Character.Tag));

                CharacterType characterType;
                if (tag.StartsWith("Character.Player."))
                {
                    characterType = CharacterType.PlayerVariant;
                }
                else if (tag.StartsWith("Character."))
                {
                    characterType = CharacterType.NPC;
                }
                else
                {
                    // All other character tags are invalid - ignore them.
                    continue;
                }

                var character = new Character(
                    CharacterId: TryGetValue<string>(row, columnMap, nameof(Character.CharacterId)),
                    Name: TryGetValue<string>(row, columnMap, nameof(Character.Name)),
                    Tag: tag,
                    CharacterType: characterType);

                characters.Add(character);
            }
            catch (Exception ex)
            {
                return Result<List<Character>>.Fail($"An error occurred when loading characters from workbook")
                    .WithException(ex);
            }
        }

        return Result<List<Character>>.Ok(characters);
    }

    private Result<List<Scene>> LoadScenes(IXLWorksheet scenesSheet)
    {
        // Find the used range
        var range = scenesSheet.RangeUsed();
        if (range == null)
        {
            return Result<List<Scene>>.Fail("The sheet is empty.");
        }

        //
        // Read header row and map column names to indexes
        //

        var headerRow = range.FirstRowUsed();
        var columnMap = new Dictionary<string, int>();

        foreach (var cell in headerRow.CellsUsed())
        {
            var columnName = cell.GetValue<string>().Trim();
            if (!string.IsNullOrEmpty(columnName))
            {
                columnMap[columnName] = cell.Address.ColumnNumber;
            }
        }

        //
        // Validate required columns
        //

        string[] requiredColumns = { "Category", "Namespace", "Context", "AssetPath" };
        foreach (var col in requiredColumns)
        {
            if (!columnMap.ContainsKey(col))
            {
                return Result<List<Scene>>.Fail($"Missing required column '{col}'");
            }
        }

        //
        // Read data rows, skipping the header
        //
        var scenes = new List<Scene>();

        foreach (var row in range.RowsUsed().Skip(1))
        {
            try
            {
                var scene = new Scene
                (
                    Category: TryGetValue<string>(row, columnMap, nameof(Scene.Category)),
                    Namespace: TryGetValue<string>(row, columnMap, nameof(Scene.Namespace)),
                    Context: TryGetValue<string>(row, columnMap, nameof(Scene.Context)),
                    AssetPath: TryGetValue<string>(row, columnMap, nameof(Scene.AssetPath))
                );

                scenes.Add(scene);
            }
            catch (Exception ex)
            {
                return Result<List<Scene>>.Fail($"An error occurred when loading scenes from workbook")
                    .WithException(ex);
            }
        }

        return Result<List<Scene>>.Ok(scenes);
    }

    private Result<List<DialogueLine>> LoadLines(IXLWorksheet linesSheet, List<Character> characters)
    {
        var lines = new List<DialogueLine>();

        // Find the used range
        var range = linesSheet.RangeUsed();
        if (range == null)
        {
            return Result<List<DialogueLine>>.Fail("The sheet is empty.");
        }

        //
        // Read header row and map column names to indexes
        //

        var headerRow = range.FirstRowUsed();
        var columnMap = new Dictionary<string, int>();

        foreach (var cell in headerRow.CellsUsed())
        {
            var columnName = cell.GetValue<string>().Trim();
            if (!string.IsNullOrEmpty(columnName))
            {
                columnMap[columnName] = cell.Address.ColumnNumber;
            }
        }

        //
        // Validate required columns
        //

        string[] requiredColumns = { "DialogueKey", "Namespace", "Category", "SourceText", "ContextNotes" };
        foreach (var col in requiredColumns)
        {
            if (!columnMap.ContainsKey(col))
            {
                return Result<List<DialogueLine>>.Fail($"Missing required column '{col}'");
            }
        }

        //
        // Read data rows, skipping the header
        //

        foreach (var row in range.RowsUsed().Skip(1))
        {
            try
            {
                var characterId = TryGetValue<string>(row, columnMap, nameof(DialogueLine.CharacterId));

                // Determine the line type
                var lineTypeResult = GetLineType(characterId, characters);
                if (lineTypeResult.IsFailure)
                {
                    return Result<List<DialogueLine>>.Fail($"Failed to determine line type at row '{row}'")
                        .WithErrors(lineTypeResult);
                }
                var lineType = lineTypeResult.Value;

                var category = TryGetValue<string> (row, columnMap, nameof(DialogueLine.Category));

                if (category == "Bark")
                {
                    // The Screenplay system only supports editing dialogue lines currently
                    continue;
                }

                var line = new DialogueLine
                (
                    LineType: lineType,
                    DialogueKey: TryGetValue<string>(row, columnMap, nameof(DialogueLine.DialogueKey)),
                    Category: category,
                    Namespace: TryGetValue<string>(row, columnMap, nameof(DialogueLine.Namespace)),
                    CharacterId: TryGetValue<string>(row, columnMap, nameof(DialogueLine.CharacterId)),
                    SpeakingTo: TryGetValue<string>(row, columnMap, nameof(DialogueLine.SpeakingTo)),
                    SourceText: TryGetValue<string>(row, columnMap, nameof(DialogueLine.SourceText)),
                    ContextNotes: TryGetValue<string>(row, columnMap, nameof(DialogueLine.ContextNotes)),
                    Direction: TryGetValue<string>(row, columnMap, nameof(DialogueLine.Direction)),
                    GameArea: TryGetValue<string>(row, columnMap, nameof(DialogueLine.GameArea)),
                    TimeConstraint: TryGetValue<string>(row, columnMap, nameof(DialogueLine.TimeConstraint)),
                    SoundProcessing: TryGetValue<string>(row, columnMap, nameof(DialogueLine.SoundProcessing)),
                    Platform: TryGetValue<string>(row, columnMap, nameof(DialogueLine.Platform)),
                    LinePriority: TryGetValue<string>(row, columnMap, nameof(DialogueLine.LinePriority)),
                    ProductionStatus: TryGetValue<string>(row, columnMap, nameof(DialogueLine.ProductionStatus))
                );

                lines.Add(line);
            }
            catch (Exception ex)
            {
                return Result<List<DialogueLine>>.Fail($"An error occurred when loading lines from workbook")
                    .WithException(ex);
            }
        }

        return Result<List<DialogueLine>>.Ok(lines);
    }

    private static Result<string> GetLineType(string characterId, List<Character> characters)
    {
        if (characterId == "SceneNote")
        {
            return Result<string>.Ok("SceneNote");
        }
        else if (characterId == "Player")
        {
            return Result<string>.Ok("Player");
        }
        else
        {
            foreach (var character in characters)
            {
                if (character.CharacterId == characterId)
                {
                    if (character.Tag.StartsWith("Character.Player."))
                    {
                        return Result<string>.Ok("PlayerVariant");
                    }
                    else
                    {
                        return Result<string>.Ok("NPC");
                    }
                }
            }
        }
        
        return Result<string>.Fail($"Failed to determine line type for character id '{characterId}'");
    }

    private Result ValidateDialogue(List<Scene> scenes, List<Character> characters, List<DialogueLine> lines)
    {
        var existingDialogueKeys = new HashSet<string>();

        for (int i = 0; i < lines.Count; i++)
        {
            var row_index = i + 1;
            DialogueLine line = lines[i];

            // Check the category is valid
            switch (line.Category)
            {
                case "Conversation":
                case "Cinematic":
                case "Bark":
                    break;
                default:
                    return Result.Fail($"Invalid category '{line.Category}' at row {row_index}");
            };

            // Check a namespace is specified, and namespaces are contiguous
            if (string.IsNullOrWhiteSpace(line.Namespace))
            {
                return Result.Fail($"Invalid namespace at row {row_index}");
            }

            // Check the line namespace matches a scene namespace
            var foundScene = false;
            foreach (var scene in scenes)
            {
                if (scene.Namespace == line.Namespace)
                {
                    foundScene = true;
                    break;
                }
            }
            if (!foundScene)
            {
                return Result.Fail($"Namespace '{line.Namespace}' not found in scenes list at row {row_index}");
            }

            // Check that the referenced character exists
            var characterId = line.CharacterId;
            if (characterId != "Player" && characterId != "SceneNote")
            {
                if (string.IsNullOrEmpty(characterId) ||
                    !characters.Any(c => c.CharacterId == characterId))
                {
                    return Result.Fail($"Character '{characterId}' not found in characters list at row {row_index}");
                }
            }

            // Check that the dialogue key is valid.
            // Bark lines have already been excluded so all dialogue keys should have exactly 3 parts.
            var dialogueKey = line.DialogueKey;
            var keyParts = dialogueKey.Split('-');
            if (keyParts.Length != 3 || keyParts.Any((p) => p.Length == 0))
            {
                return Result.Fail($"DialogueKey '{dialogueKey}' does not contain 3 parts at row {row_index}");
            }

            if (characterId != keyParts[0])
            {
                return Result.Fail($"DialogueKey '{dialogueKey}' does not match character id '{characterId}' at row {row_index}");
            }
            if (line.Namespace != keyParts[1])
            {
                return Result.Fail($"DialogueKey '{dialogueKey}' does not match namespace '{line.Namespace}' at row {row_index}");
            }

            var lineId = keyParts[2];
            if (string.IsNullOrEmpty(lineId))
            {
                return Result.Fail($"Line id is empty at row {row_index}");
            }

            if (existingDialogueKeys.Contains(dialogueKey))
            {
                return Result.Fail($"Duplicate dialogue key '{dialogueKey}' at row {row_index}");
            }

            // Check that there are no duplicate dialogue keys
            existingDialogueKeys.Add(dialogueKey);
        }

        return Result.Ok();
    }

    private Result AddSceneLines(List<Scene> scenes, List<DialogueLine> lines)
    {
        var namespace_lines = new Dictionary<string, List<DialogueLine>>();

        var previous_namespace = string.Empty;
        int row_index = 1;
        foreach (var line in lines)
        {
            var namespace_key = line.Namespace;

            // Acquire a list of lines for the namespace
            List<DialogueLine> filteredLines;
            if (!namespace_lines.TryGetValue(namespace_key, out filteredLines!))
            {
                filteredLines = new List<DialogueLine>();
                namespace_lines[namespace_key] = filteredLines;
            }
            else
            {
                // Check that namespaces are defined contiguously
                if (previous_namespace != namespace_key)
                {
                    // This namespace has already been processed, but the previous line's namespace
                    // does not match the current line's namespace.
                    return Result.Fail($"Non-contiguous namespace at row {row_index}");
                }
            }

            // Add the line to the list of lines for the namespace
            filteredLines.Add(line);

            previous_namespace = namespace_key;
            row_index++;
        }

        foreach (var scene in scenes)
        {
            var namespace_key = scene.Namespace;

            // The scene should not contain any lines yet
            if (scene.Lines.Count > 0)
            {
                return Result.Fail($"Scene '{namespace_key}' already has dialogue lines");
            }

            // Add any lines that were found to the scene.
            // If there are no lines then this is probably a newly added Dialogue Asset.
            if (namespace_lines.TryGetValue(namespace_key, out var dialogue_lines))
            {
                scene.Lines.AddRange(dialogue_lines);
            }
        }

        return Result.Ok();
    }

    private async Task<Result> SaveSceneFilesAsync(List<Scene> scenes, ResourceKey workbookResource)
    {
        var screenplayFolderResource = Path.GetFileNameWithoutExtension(workbookResource);

        var resourceRegistry = _explorerService.ResourceRegistry;
        var sceneFolderPath = resourceRegistry.GetResourcePath(screenplayFolderResource);

        var entityService = _workspaceWrapper.WorkspaceService.EntityService;
        var entityFolderPath = entityService.GetEntityDataPath(screenplayFolderResource);

        // Remove the .json extension
        entityFolderPath = entityFolderPath.Substring(0, entityFolderPath.LastIndexOf('.'));

        // Save a .scene file for each namespace
        foreach (var scene in scenes)
        {
            var category = scene.Category;
            if (category == "Bark")
            {
                // Editing barks is not supported yet
                continue;
            }

            // Create the .scene resource and entity data

            var saveFileResult = await SaveSceneFileAsync(sceneFolderPath, category, scene.AssetPath);
            if (saveFileResult.IsFailure)
            {
                var filename = Path.GetFileName(scene.AssetPath);
                return Result.Fail($"Failed to save scene file '{filename}'")
                    .WithErrors(saveFileResult);
            }

            var saveEntityResult = await SaveSceneEntityFileAsync(workbookResource, entityFolderPath, category, scene.Namespace, scene.Context, scene.AssetPath, scene.Lines);
            if (saveEntityResult.IsFailure)
            {
                var filename = Path.GetFileName(scene.AssetPath);
                return Result.Fail($"Failed to save scene entity file '{filename}'")
                    .WithErrors(saveEntityResult);
            }
        }

        return Result.Ok();
    }

    private async Task<Result> SaveSceneFileAsync(string sceneFolderPath, string category, string assetPath)
    {
        try
        {
            // Create a .scene file

            var subFolder = Path.GetDirectoryName(assetPath) ?? string.Empty;
            var assetName = Path.GetFileNameWithoutExtension(assetPath);
            var sceneFilePath = Path.Combine(sceneFolderPath, category, subFolder, $"{assetName}.scene");
            var sceneFolder = Path.GetDirectoryName(sceneFilePath);

            if (!string.IsNullOrEmpty(sceneFolder) &&
                !Directory.Exists(sceneFolder))
            {
                Directory.CreateDirectory(sceneFolder);
            }

            await File.WriteAllTextAsync(sceneFilePath, string.Empty);
        }
        catch (Exception ex)
        {
            return Result.Fail($"An error occurred when saving scene file")
                .WithException(ex);
        }

        return Result.Ok();
    }

    private async Task<Result> SaveSceneEntityFileAsync(ResourceKey workbookResource, string entityFolderPath, string category, string @namespace, string context, string assetPath, List<DialogueLine> lineList)
    {
        try
        {
            var subFolder = Path.GetDirectoryName(assetPath) ?? string.Empty;
            var assetName = Path.GetFileNameWithoutExtension(assetPath);
            var entityFilePath = Path.Combine(entityFolderPath, category, subFolder, $"{assetName}.scene.json");
            var entityFolder = Path.GetDirectoryName(entityFilePath);

            if (!string.IsNullOrEmpty(entityFolder) &&
                !Directory.Exists(entityFolder))
            {
                Directory.CreateDirectory(entityFolder);
            }

            // Generate component data for each line

            var components = new JsonArray();

            // Add scene component
            var sceneComponent = new JsonObject();
            sceneComponent["_type"] = "Screenplay.Scene#1";
            sceneComponent["dialogueFile"] = workbookResource.ToString();
            sceneComponent["category"] = category;
            sceneComponent["namespace"] = @namespace;
            sceneComponent["context"] = context;
            components.Add(sceneComponent);

            // Add line components
            // The line list may be empty if this is a newly created scene.
            foreach (var line in lineList)
            {
                var dialogueKey = line.DialogueKey;

                // Extract the line id
                var lineIdIndex = dialogueKey.LastIndexOf('-');

                // Todo: Move these checks to the validation step
                Guard.IsFalse(lineIdIndex == -1);
                Guard.IsFalse(dialogueKey.Length == lineIdIndex + 1);
                var lineId = dialogueKey.Substring(lineIdIndex + 1);

                if (line.CharacterId == "SceneNote")
                {
                    var lineComponent = new JsonObject();
                    lineComponent["_type"] = "Screenplay.Line#1";
                    lineComponent["lineType"] = line.LineType;
                    lineComponent["lineId"] = lineId;
                    lineComponent["characterId"] = line.CharacterId;
                    lineComponent["speakingTo"] = string.Empty;
                    lineComponent["sourceText"] = line.SourceText;
                    lineComponent["contextNotes"] = string.Empty;
                    lineComponent["direction"] = string.Empty;
                    lineComponent["gameArea"] = string.Empty;
                    lineComponent["timeConstraint"] = string.Empty;
                    lineComponent["soundProcessing"] = string.Empty;
                    lineComponent["platform"] = string.Empty;
                    lineComponent["linePriority"] = string.Empty;
                    lineComponent["productionStatus"] = string.Empty;

                    components.Add(lineComponent);
                }
                else
                {
                    var lineComponent = new JsonObject();
                    lineComponent["_type"] = "Screenplay.Line#1";
                    lineComponent["lineType"] = line.LineType;
                    lineComponent["lineId"] = lineId;
                    lineComponent["characterId"] = line.CharacterId;
                    lineComponent["speakingTo"] = line.SpeakingTo;
                    lineComponent["sourceText"] = line.SourceText;
                    lineComponent["contextNotes"] = line.ContextNotes;
                    lineComponent["direction"] = line.Direction;
                    lineComponent["gameArea"] = line.GameArea;
                    lineComponent["timeConstraint"] = line.TimeConstraint;
                    lineComponent["soundProcessing"] = line.SoundProcessing;
                    lineComponent["platform"] = line.Platform;
                    lineComponent["linePriority"] = line.LinePriority;
                    lineComponent["productionStatus"] = line.ProductionStatus;

                    components.Add(lineComponent);
                }
            }

            var entity = new JsonObject();
            entity["_entityVersion"] = 1;
            entity["_components"] = components;

            var options = new JsonSerializerOptions()
            {
                WriteIndented = true,
                DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            };
            var entityData = entity.ToJsonString(options);

            await File.WriteAllTextAsync(entityFilePath, entityData);
        }
        catch (Exception ex) 
        {
            return Result.Fail($"An error occurred when saving a scene entity file")
                .WithException(ex);

        }

        return Result.Ok();
    }

    private Result PopulateCharacters(IComponentProxy screenplayData, List<Character> characters)
    {
        // Populate the "characters" property of the ScreenplayData component with a JSON object
        // mapping character IDs to character names and tags.

        var charactersObject = new JsonObject();

        // Add the 'Player' character
        charactersObject["Player"] = new JsonObject
        {
            ["name"] = "Player",
            ["tag"] = "Character.Player"
        };

        // Add the characters from the 'Characters' sheet
        foreach (var character in characters)
        {
            // Check for empty name or tag
            if (string.IsNullOrEmpty(character.Name))
            {
                return Result.Fail($"Empty character name '{character.CharacterId}'");
            }

            if (string.IsNullOrEmpty(character.Tag))
            {
                return Result.Fail($"Empty character tag '{character.CharacterId}'");
            }

            // Add character
            charactersObject[character.CharacterId] = new JsonObject
            {
                ["name"] = character.Name,
                ["tag"] = character.Tag
            };
        }

        var options = new JsonSerializerOptions()
        {
            WriteIndented = true
        };
        var charactersJson = charactersObject.ToJsonString(options);

        var setResult = screenplayData.SetProperty("characters", charactersJson);
        if (setResult.IsFailure)
        {
            return Result.Fail($"Failed to populate characters property on ScreenplayData component")
                .WithErrors(setResult);
        }

        return Result.Ok();
    }

    private T TryGetValue<T>(IXLRangeRow row, Dictionary<string, int> columnMap, string columnName)
    {
        if (!columnMap.TryGetValue(columnName, out int colIndex))
            throw new Exception($"Missing column '{columnName}'");

        var cell = row.Cell(colIndex);
        try
        {
            return cell.GetValue<T>();
        }
        catch
        {
            throw new Exception($"Invalid data in column '{columnName}' at row {row.RowNumber()}");
        }
    }
}
