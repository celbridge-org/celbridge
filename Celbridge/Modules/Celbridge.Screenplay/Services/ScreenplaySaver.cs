using Celbridge.Entities;
using Celbridge.Explorer;
using Celbridge.Screenplay.Components;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Screenplay.Services;

public class ScreenplaySaver
{
    private const string CinematicColor = "f6d6ad";
    private const string ConversationColor = "f4b6c2";
    private const string BarkColor = "ccc0da";
    private const string NamespaceColorA = "b5d8f6";
    private const string NamespaceColorB = "dce6f1";
    private const string PlayerColor = "c0c7d6";
    private const string PlayerVariantColor = "e3e3e3";
    private const string SceneNoteColor = "a8e6a3";

    private IExplorerService _explorerService;
    private IWorkspaceWrapper _workspaceWrapper;

    private record SceneData(string Category, string Namespace, IComponentProxy SceneComponent, List<IComponentProxy> DialogueComponents);

    public ScreenplaySaver(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
        _explorerService = workspaceWrapper.WorkspaceService.ExplorerService;
    }

    public Result SaveScreenplay(ResourceKey screenplayResource)
    {
        try
        {
            var extension = Path.GetExtension(screenplayResource);
            if (extension != ".xlsx")
            {
                return Result.Fail($"Unsupported file type: {extension}");
            }

            var entityService = _workspaceWrapper.WorkspaceService.EntityService;
            var resourceRegistry = _explorerService.ResourceRegistry;
            var workbookFilePath = resourceRegistry.GetResourcePath(screenplayResource);

            var screenplayFolderPath = Path.GetDirectoryName(workbookFilePath);
            if (string.IsNullOrEmpty(screenplayFolderPath))
            {
                return Result.Fail($"Failed to get screenplay folder path for resource '{screenplayResource}'");
            }

            // Acquire the ScreenplayData component from the screenplay resource
            var getComponentResult = entityService.GetComponentOfType(screenplayResource, ScreenplayDataEditor.ComponentType);
            if (getComponentResult.IsFailure)
            {
                return Result.Fail($"Failed to get ScreenplayData component from workbook file resource '{screenplayResource}'")
                    .WithErrors(getComponentResult);
            }
            var screenplayData = getComponentResult.Value;

            // Find all .scene files in the screenplay folder
            var sceneFiles = Directory.GetFiles(screenplayFolderPath, "*.scene", SearchOption.AllDirectories).ToList();
            if (sceneFiles.Count == 0)
            {
                return Result.Fail($"No scene files found in folder '{screenplayFolderPath}'");
            }

            // Collect all dialogue lines for all scene files found
            var collectResult = CollectSceneData(entityService, sceneFiles);
            if (collectResult.IsFailure)
            {
                return Result.Fail($"Failed to collect dialogue data from scene files")
                    .WithErrors(collectResult);
            }
            var sceneDataList = collectResult.Value;

            return SaveDialogueWorksheet(workbookFilePath, sceneDataList);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to import screenplay data from workbook")
                .WithException(ex);
        }
    }

    private Result SaveDialogueWorksheet(string workbookFilePath, List<SceneData> sceneDataList)
    {
        // Open the workbook file.
        // It's best to do this before we make any other changes, e.g. in case the file is locked.
        using var workbook = new XLWorkbook(workbookFilePath);

        var dialogueSheet = workbook.Worksheet("Dialogue");

        if (workbook.Worksheets.Contains("TempDialogue"))
        {
            // Delete the existing "TempDialogue" worksheet
            workbook.Worksheet("TempDialogue").Delete();
            workbook.Save();
        }

        // Duplicate the "Dialogue" worksheet to a temp worksheet.
        var editedSheet = dialogueSheet.CopyTo("TempDialogue");

        // Find the last row containing dialogue lines
        var lastRow = editedSheet.LastRowUsed()?.RowNumber() ?? 1;

        // Delete all rows except the header
        var range = editedSheet.Range(2, 1, lastRow, 14);
        range.Clear();

        // Output all conversation dialogue lines to the "TempDialogue" spreadsheet.
        int namespaceIndex = 0;
        int rowIndex = 2;
        foreach (var sceneData in sceneDataList)
        {
            var sceneCategory = sceneData.Category;
            var sceneNamespace = sceneData.Namespace;

            var categoryColor = sceneCategory switch
            {
                "Cinematic" => CinematicColor,
                "Conversation" => ConversationColor,
                "Bark" => BarkColor,
                _ => "FFFFFF"
            };

            var namespaceColor = namespaceIndex % 2 == 1 ? NamespaceColorA : NamespaceColorB;
            namespaceIndex++;

            var playerLineId = string.Empty;
            int sceneNoteIndex = 1;
            foreach (var dialogueComponent in sceneData.DialogueComponents)
            {
                editedSheet.Cell(rowIndex, 1).Value = sceneCategory;
                editedSheet.Cell(rowIndex, 1).Style.Fill.BackgroundColor = XLColor.FromHtml(categoryColor);

                editedSheet.Cell(rowIndex, 2).Value = sceneNamespace;
                editedSheet.Cell(rowIndex, 2).Style.Fill.BackgroundColor = XLColor.FromHtml(namespaceColor);

                if (dialogueComponent.Schema.ComponentType == LineEditor.ComponentType)
                {
                    var characterId = dialogueComponent.GetString(LineEditor.CharacterId);
                    var dialogueKey = dialogueComponent.GetString(LineEditor.DialogueKey);
                    var separatorIndex = dialogueKey.LastIndexOf('-');
                    var lineId = separatorIndex >= 0 ? dialogueKey.Substring(separatorIndex + 1) : dialogueKey;

                    editedSheet.Cell(rowIndex, 3).Value = dialogueKey;

                    editedSheet.Cell(rowIndex, 4).Value = characterId;
                    if (characterId == "Player")
                    {
                        // Player line
                        playerLineId = lineId;

                        editedSheet.Cell(rowIndex, 3).Style.Fill.BackgroundColor = XLColor.FromHtml(PlayerColor);
                        editedSheet.Cell(rowIndex, 4).Style.Fill.BackgroundColor = XLColor.FromHtml(PlayerColor);
                    }
                    else if (lineId == playerLineId)
                    {
                        // Player variant line
                        editedSheet.Cell(rowIndex, 3).Style.Fill.BackgroundColor = XLColor.FromHtml(PlayerVariantColor);
                        editedSheet.Cell(rowIndex, 4).Style.Fill.BackgroundColor = XLColor.FromHtml(PlayerVariantColor);
                    }
                    else
                    {
                        // NPC line
                        playerLineId = string.Empty;
                    }

                    var sourceText = dialogueComponent.GetString(LineEditor.SourceText);
                    if (sourceText.StartsWith("'") && 
                        !sourceText.StartsWith("''"))
                    {
                        // Excel treats cells that start with an apostrophe as text.
                        // Escape the leading apostrophe character so it will import correctly. 
                        sourceText = $"'{sourceText}";
                    }

                    editedSheet.Cell(rowIndex, 5).Value = dialogueComponent.GetString(LineEditor.SpeakingTo);
                    editedSheet.Cell(rowIndex, 6).Value = sourceText;
                    editedSheet.Cell(rowIndex, 7).Value = dialogueComponent.GetString(LineEditor.ContextNotes);
                    editedSheet.Cell(rowIndex, 8).Value = dialogueComponent.GetString(LineEditor.Direction);
                    editedSheet.Cell(rowIndex, 9).Value = dialogueComponent.GetString(LineEditor.GameArea);
                    editedSheet.Cell(rowIndex, 10).Value = dialogueComponent.GetString(LineEditor.TimeConstraint);
                    editedSheet.Cell(rowIndex, 11).Value = dialogueComponent.GetString(LineEditor.SoundProcessing);
                    editedSheet.Cell(rowIndex, 12).Value = dialogueComponent.GetString(LineEditor.Platform);
                    editedSheet.Cell(rowIndex, 13).Value = dialogueComponent.GetString(LineEditor.LinePriority);
                    editedSheet.Cell(rowIndex, 14).Value = dialogueComponent.GetString(LineEditor.ProductionStatus);
                }
                else if (dialogueComponent.Schema.ComponentType == EntityConstants.EmptyComponentType)
                {
                    // Scene note
                    playerLineId = string.Empty;

                    var commentText = dialogueComponent.GetString("/comment");

                    var dialogueKey = $"SceneNote-{sceneNamespace}-Note{sceneNoteIndex}";
                    editedSheet.Cell(rowIndex, 3).Value = dialogueKey;
                    editedSheet.Cell(rowIndex, 4).Value = "SceneNote";
                    editedSheet.Cell(rowIndex, 6).Value = commentText;

                    for (int i = 3; i <= 14; i++)
                    {
                        editedSheet.Cell(rowIndex, i).Style.Fill.BackgroundColor = XLColor.FromHtml(SceneNoteColor);
                    }

                    sceneNoteIndex++;
                }
                rowIndex++;
            }
        }

        // Copy all bark dialogue lines from the "Dialogue" sheet to the bottom of the "TempDialogue" sheet
        var startBarkRow = -1;
        for (int i = 2; i <= lastRow; i++)
        {
            // Find fisrt row containing bark dialogue
            var category = dialogueSheet.Cell(i, 1).Value.ToString();
            if (category == "Bark")
            {
                startBarkRow = i;
                break;
            }
        }
        if (startBarkRow >= 0)
        {
            var barkRange = dialogueSheet.Range(startBarkRow, 1, lastRow, 14);
            barkRange.CopyTo(editedSheet.Cell(rowIndex, 1));
        }

        // Set the scroll position when the sheet is first opened
        editedSheet.SheetView.TopLeftCellAddress = editedSheet.FirstCell().Address;
        editedSheet.SelectedRanges.RemoveAll();
        editedSheet.FirstCell().SetActive();

        // Replace the "Dialogue" sheet with the "TempDialogue" sheet
        workbook.Worksheets.Delete(dialogueSheet.Name);
        editedSheet.Name = dialogueSheet.Name;
        editedSheet.Position = 1;

        // Save the workbook
        workbook.Save();

        return Result.Ok();
    }

    private Result<List<SceneData>> CollectSceneData(IEntityService entityService, IReadOnlyList<string> sceneFiles)
    {
        var processedNamespaces = new HashSet<string>();

        // Build a dictionary mapping each scene namespace to it's list of dialogue components
        var sceneDataList = new List<SceneData>();
        foreach (var sceneFile in sceneFiles)
        {
            // Get the resource key for the scene file
            var getResourceResult = _explorerService.ResourceRegistry.GetResourceKey(sceneFile);
            if (getResourceResult.IsFailure)
            {
                return Result<List<SceneData>>.Fail($"Failed to get resource key for scene file '{sceneFile}'")
                    .WithErrors(getResourceResult);
            }
            var sceneResource = getResourceResult.Value;

            // Get all components in the scene entity
            var getComponentsResult = entityService.GetComponents(sceneResource);
            if (getComponentsResult.IsFailure)
            {
                Result<List<SceneData>>.Fail($"Failed to get components for scene file '{sceneFile}'")
                    .WithErrors(getComponentsResult);
            }
            var components = getComponentsResult.Value;

            // Get the Scene root component for the scene file
            if (components.Count == 0)
            {
                Result<List<SceneData>>.Fail($"No components found for scene file '{sceneFile}'");
            }
            var sceneComponent = components[0];
            if (sceneComponent.Schema.ComponentType != SceneEditor.ComponentType)
            {
                Result<List<SceneData>>.Fail($"Failed to get Scene root Component for scene file '{sceneFile}'");
            }

            var sceneCategory = sceneComponent.GetString(SceneEditor.Category);
            if (sceneCategory != "Cinematic" &&
                sceneCategory != "Conversation" &&
                sceneCategory != "Bark")
            {
                Result<List<SceneData>>.Fail($"Invalid category '{sceneCategory}'");
            }

            // Get the scene namespace
            var sceneNamespace = sceneComponent.GetString(SceneEditor.Namespace);
            if (processedNamespaces.Contains(sceneNamespace))
            {
                return Result<List<SceneData>>.Fail($"Duplicate declaration of namespace '{sceneNamespace}' in scene file '{sceneFile}'.");
            }
            processedNamespaces.Add(sceneNamespace);

            // Build list of all Line and Empty components in the scene entity
            var dialogueComponents = new List<IComponentProxy>();
            foreach (var component in components)
            {
                if (component.Schema.ComponentType == LineEditor.ComponentType ||
                    component.Schema.ComponentType == EntityConstants.EmptyComponentType)
                {
                    // Add the component to the list of dialogue components
                    dialogueComponents.Add(component);
                }
            }

            // Add the scene data to the list
            var sceneData = new SceneData(sceneCategory, sceneNamespace, sceneComponent, dialogueComponents);
            sceneDataList.Add(sceneData);
        }

        // Sort the scene data list
        sceneDataList = sceneDataList.OrderBy(sceneData => sceneData.Category)
            .ThenBy(sceneData => sceneData.Namespace)
            .ToList();

        return Result<List<SceneData>>.Ok(sceneDataList);
    }
}
