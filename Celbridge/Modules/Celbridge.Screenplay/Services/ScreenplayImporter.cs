using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Screenplay.Models;
using Celbridge.Workspace;
using ClosedXML.Excel;
using System.Text.Json.Nodes;
using System.Text.Json;
using Celbridge.Screenplay.Components;
using Celbridge.Entities;

namespace Celbridge.Screenplay.Services;

public class ScreenplayImporter
{
    private ILogger<ScreenplayImporter> _logger;
    private IExplorerService _explorerService;
    private IWorkspaceWrapper _workspaceWrapper;

    public ScreenplayImporter(
        ILogger<ScreenplayImporter> logger,
        IExplorerService explorerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _explorerService = explorerService;
        _workspaceWrapper = workspaceWrapper;
    }

    public async Task<Result> ImportScreenplayAsync(ResourceKey excelFile)
    {
        try
        {
            var extension = Path.GetExtension(excelFile);
            if (extension != ".xlsx")
            {
                return Result.Fail($"Unsupported file type: {extension}");
            }

            var entityService = _workspaceWrapper.WorkspaceService.EntityService;
            var resourceRegistry = _explorerService.ResourceRegistry;
            var excelFilePath = resourceRegistry.GetResourcePath(excelFile);
            var screenplayFolderPath = Path.GetFileNameWithoutExtension(excelFilePath);

            // Acquire the ScreenplayData component from the Excel file resource
            var getComponentResult = entityService.GetComponentOfType(excelFile, ScreenplayDataEditor.ComponentType);
            if (getComponentResult.IsFailure)
            {
                return Result.Fail($"Failed to get ScreenplayData component from Excel file resource '{excelFile}'")
                    .WithErrors(getComponentResult);
            }
            var screenplayData = getComponentResult.Value;

            // Open the Excel file.
            // It's best to do this before we make any other changes, e.g. in case the file is locked.
            using var workbook = new XLWorkbook(excelFilePath);

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
            var loadCharactersResult = ReadCharacters(charactersWorksheet);
            if (loadCharactersResult.IsFailure)
            {
                return Result.Fail($"Failed to load characters from Excel")
                    .WithErrors(loadCharactersResult);
            }
            var characters = loadCharactersResult.Value;

            // Load the dialogue lines from the "Lines" worksheet
            var linesWorksheet = workbook.Worksheet("Lines");
            var loadLinesResult = ReadLines(linesWorksheet);
            if (loadLinesResult.IsFailure)
            {
                return Result.Fail($"Failed to load dialogue lines from Excel")
                    .WithErrors(loadLinesResult);
            }
            var lines = loadLinesResult.Value;

            // Validate imported data
            var validateResult = ValidateLines(characters, lines);
            if (validateResult.IsFailure)
            {
                return Result.Fail($"Failed to validate imported dialogue data")
                    .WithErrors(validateResult);
            }

            // Split the dialogue lines by namespace
            var createResult = CreateNamespaceLines(lines);
            if (createResult.IsFailure)
            {
                return Result.Fail($"Failed create namespace lines dictionary")
                    .WithErrors(createResult);
            }
            var namespaceLines = createResult.Value;

            // Save a .scene file for each namespace
            var saveResult = await SaveSceneFilesAsync(namespaceLines, excelFile);
            if (saveResult.IsFailure)
            {
                return Result.Fail($"Failed to save .scene files")
                    .WithErrors(loadLinesResult);
            }

            var populateResult = PopulateCharacters(screenplayData, characters);
            if (populateResult.IsFailure)
            {
                return Result.Fail($"Failed to populate characters property")
                    .WithErrors(loadLinesResult);
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to import screenplay data from Excel")
                .WithException(ex);
        }
    }

    private Result<List<Character>> ReadCharacters(IXLWorksheet characterSheet)
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
                var character = new Character
                (
                    CharacterId: TryGetValue<string>(row, columnMap, nameof(Character.CharacterId)),
                    Name: TryGetValue<string>(row, columnMap, nameof(Character.Name)),
                    Tag: TryGetValue<string>(row, columnMap, nameof(Character.Tag))
                );

                characters.Add(character);
            }
            catch (Exception ex)
            {
                return Result<List<Character>>.Fail($"An error occurred when reading characters from Excel")
                    .WithException(ex);
            }
        }

        return Result<List<Character>>.Ok(characters);
    }

    private Result<List<DialogueLine>> ReadLines(IXLWorksheet linesSheet)
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
                var line = new DialogueLine
                (
                    DialogueKey: TryGetValue<string>(row, columnMap, nameof(DialogueLine.DialogueKey)),
                    Category: TryGetValue<string>(row, columnMap, nameof(DialogueLine.Category)),
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
                    ProductionStatus: TryGetValue<string>(row, columnMap, nameof(DialogueLine.ProductionStatus)),
                    DialogueAsset: TryGetValue<string>(row, columnMap, nameof(DialogueLine.DialogueAsset))
                );

                lines.Add(line);
            }
            catch (Exception ex)
            {
                return Result<List<DialogueLine>>.Fail($"An error occurred when reading lines from Excel")
                    .WithException(ex);
            }
        }

        return Result<List<DialogueLine>>.Ok(lines);
    }

    private Result ValidateLines(List<Character> characters, List<DialogueLine> lines)
    {
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

            // Check that the referenced character exists
            var characterId = line.CharacterId;
            if (string.IsNullOrEmpty(characterId) ||
                !characters.Any(c => c.CharacterId == characterId))
            {
                return Result.Fail($"Character '{characterId}' not found in characters list at row {row_index}");
            }
        }

        return Result.Ok();
    }

    private Result<Dictionary<string, List<DialogueLine>>> CreateNamespaceLines(List<DialogueLine> lines)
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
                    return Result<Dictionary<string, List<DialogueLine>>>.Fail($"Non-contiguous namespace at row {row_index}");
                }
            }

            // Add the line to the list of lines for the namespace
            filteredLines.Add(line);

            previous_namespace = namespace_key;
            row_index++;
        }

        return Result<Dictionary<string, List<DialogueLine>>>.Ok(namespace_lines);
    }

    private async Task<Result> SaveSceneFilesAsync(Dictionary<string, List<DialogueLine>> namespaceLines, ResourceKey excelResource)
    {
        var screenplayFolderResource = Path.GetFileNameWithoutExtension(excelResource);

        var resourceRegistry = _explorerService.ResourceRegistry;
        var sceneFolderPath = resourceRegistry.GetResourcePath(screenplayFolderResource);

        var entityService = _workspaceWrapper.WorkspaceService.EntityService;
        var entityFolderPath = entityService.GetEntityDataPath(screenplayFolderResource);

        // Remove the .json extension
        entityFolderPath = entityFolderPath.Substring(0, entityFolderPath.LastIndexOf('.'));

        // Save a .scene file for each namespace
        foreach (var kv in namespaceLines)
        {
            var namespaceKey = kv.Key;
            var lineList = kv.Value;

            if (lineList.Count == 0)
            {
                continue;
            }

            // Get the category from the first line in the list
            var category = lineList[0].Category;

            if (category == "Bark")
            {
                // Editing barks is not supported yet
                continue;
            }

            // Create the .scene resource and entity data

            await SaveSceneFileAsync(sceneFolderPath, category, namespaceKey);
            await SaveEntityFileAsync(excelResource, entityFolderPath, category, namespaceKey, lineList);
        }

        return Result.Ok();
    }

    private static async Task SaveSceneFileAsync(string sceneFolderPath, string category, string namespace_key)
    {
        // Create a .scene file
        var sceneFilePath = Path.Combine(sceneFolderPath, category, $"{namespace_key}.scene");

        var sceneFolder = Path.GetDirectoryName(sceneFilePath);
        if (!string.IsNullOrEmpty(sceneFolder) &&
            !Directory.Exists(sceneFolder))
        {
            Directory.CreateDirectory(sceneFolder);
        }

        await File.WriteAllTextAsync(sceneFilePath, string.Empty);
    }

    private static async Task SaveEntityFileAsync(ResourceKey excelResource, string entityFolderPath, string category, string namespace_key, List<DialogueLine> line_list)
    {
        var entityFilePath = Path.Combine(entityFolderPath, category, $"{namespace_key}.scene.json");

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
        sceneComponent["dialogueFile"] = excelResource.ToString();
        sceneComponent["category"] = category;
        sceneComponent["namespace"] = namespace_key;
        components.Add(sceneComponent);

        // Add line components
        foreach (var line in line_list)
        {
            var lineComponent = new JsonObject();
            lineComponent["_type"] = "Screenplay.Line#1";
            lineComponent["dialogueKey"] = line.DialogueKey;
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

    private Result PopulateCharacters(IComponentProxy screenplayData, List<Character> characters)
    {
        // Populate the "characters" property of the ScreenplayData component with a JSON object
        // mapping character IDs to character names and tags.

        var charactersObject = new JsonObject();
        foreach (var character in characters)
        {
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
