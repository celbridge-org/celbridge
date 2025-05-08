using Celbridge.Dialog;
using Celbridge.Entities;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Messaging;
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

    private readonly IMessengerService _messengerService;
    private readonly IExplorerService _explorerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private record SceneData(ResourceKey SceneResource, string Category, string Namespace, IComponentProxy SceneComponent, List<IComponentProxy> DialogueComponents);

    public ScreenplaySaver(
        IMessengerService messengerService,
        IDialogService dialogService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _messengerService = messengerService;
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

            var workbookPath = _explorerService.ResourceRegistry.GetResourcePath(screenplayResource);
            var screenplayFolder = Path.GetDirectoryName(workbookPath);
            if (string.IsNullOrEmpty(screenplayFolder))
            {
                return Result.Fail($"Failed to get screenplay folder path for resource '{screenplayResource}'");
            }

            var entityService = _workspaceWrapper.WorkspaceService.EntityService;

            // Find all .scene files in the screenplay folder
            var sceneFiles = Directory.GetFiles(screenplayFolder, "*.scene", SearchOption.AllDirectories).ToList();
            if (sceneFiles.Count == 0)
            {
                return Result.Fail($"No scene files found in folder '{screenplayFolder}'");
            }

            // Collect all dialogue lines for all scene files found
            var collectSceneDataResult = CollectSceneData(entityService, sceneFiles);
            if (collectSceneDataResult.IsFailure)
            {
                return Result.Fail("Failed to collect scene data")
                    .WithErrors(collectSceneDataResult);
            }
            var sceneDataList = collectSceneDataResult.Value;

            bool allSucceeded = true;

            var activityService = _workspaceWrapper.WorkspaceService.ActivityService;
            foreach (var sceneData in sceneDataList)
            {
                bool annotateSucceeded = true;
                var sceneResource = sceneData.SceneResource;

                var annotateResult = activityService.AnnotateEntity(sceneResource);
                if (annotateResult.IsSuccess)
                {
                    var annotation = annotateResult.Value;

                    if (annotation.TryGetError(out var entityError) &&
                        entityError!.Severity >= AnnotationErrorSeverity.Error)
                    {
                        annotateSucceeded = false;
                    }
                }
                else
                {
                    annotateSucceeded = false;
                }

                if (!annotateSucceeded)
                {
                    allSucceeded = false;

                    // Broadcast a message to notify the user about the error via the inspector panel for the ScreenplayData component.
                    var message = new SaveScreenplayErrorMessage(sceneResource);
                    _messengerService.Send(message);
                }
            }

            if (!allSucceeded)
            {
                return Result.Fail($"Failed to annotate scene resources");
            }

            var saveWorksheetResult = SaveDialogueWorksheet(workbookPath, sceneDataList);
            return saveWorksheetResult;
        }
        catch (Exception ex)
        {
            return Result.Fail("Failed to save screenplay data to workbook")
                .WithException(ex);
        }
    }

    private Result<List<SceneData>> CollectSceneData(IEntityService entityService, IReadOnlyList<string> sceneFiles)
    {
        // Build a list of SceneData based on the .scene files we found
        var processedNamespaces = new HashSet<string>();
        var sceneDataList = new List<SceneData>();

        foreach (var sceneFile in sceneFiles)
        {
            var getResourceKeyResult = _explorerService.ResourceRegistry.GetResourceKey(sceneFile);
            if (getResourceKeyResult.IsFailure)
            {
                return Result<List<SceneData>>.Fail($"Failed to get resource key for scene file '{sceneFile}'")
                    .WithErrors(getResourceKeyResult);
            }

            var sceneResource = getResourceKeyResult.Value;

            var getComponentsResult = entityService.GetComponents(sceneResource);
            if (getComponentsResult.IsFailure)
            {
                return Result<List<SceneData>>.Fail($"Failed to get components for scene file '{sceneFile}'")
                    .WithErrors(getComponentsResult);
            }
            var components = getComponentsResult.Value;

            if (components.Count == 0)
            {
                return Result<List<SceneData>>.Fail($"No components found for scene file '{sceneFile}'");
            }

            var sceneComponent = components[0];
            if (sceneComponent.Schema.ComponentType != SceneEditor.ComponentType)
            {
                return Result<List<SceneData>>.Fail($"Root component is not a Scene component for scene file '{sceneFile}'");
            }

            var category = sceneComponent.GetString(SceneEditor.Category);
            if (category != "Cinematic" && category != "Conversation" && category != "Bark")
            {
                return Result<List<SceneData>>.Fail($"Invalid category '{category}' in scene file '{sceneFile}'");
            }

            var ns = sceneComponent.GetString(SceneEditor.Namespace);
            if (processedNamespaces.Contains(ns))
            {
                return Result<List<SceneData>>.Fail($"Duplicate declaration of namespace '{ns}' in scene file '{sceneFile}'");
            }

            processedNamespaces.Add(ns);

            var dialogueComponents = components
                .Where(c => c.Schema.ComponentType == LineEditor.ComponentType ||
                            c.Schema.ComponentType == EntityConstants.EmptyComponentType)
                .ToList();

            var sceneData = new SceneData(sceneResource, category, ns, sceneComponent, dialogueComponents);
            sceneDataList.Add(sceneData);
        }

        var sortedList = sceneDataList
            .OrderBy(sd => sd.Category)
            .ThenBy(sd => sd.Namespace)
            .ToList();

        return Result<List<SceneData>>.Ok(sortedList);
    }

    private Result SaveDialogueWorksheet(string workbookFilePath, List<SceneData> sceneDataList)
    {
        // Open the workbook file.
        // It's best to do this before we make any other changes, e.g. in case the file is locked.
        using var workbook = new XLWorkbook(workbookFilePath);

        if (!workbook.Worksheets.Contains("Dialogue"))
        {
            return Result.Fail("Workbook is missing 'Dialogue' sheet");
        }

        var dialogueSheet = workbook.Worksheet("Dialogue");

        if (workbook.Worksheets.Contains("TempDialogue"))
        {
            // Delete any existing "TempDialogue" worksheet
            workbook.Worksheet("TempDialogue").Delete();
            workbook.Save();
        }

        // Duplicate the "Dialogue" worksheet to a temp worksheet.
        var editedSheet = dialogueSheet.CopyTo("TempDialogue");

        // Find the last row containing dialogue lines
        var lastRow = editedSheet.LastRowUsed()?.RowNumber() ?? 1;

        // Delete all rows except the header
        editedSheet.Range(2, 1, lastRow, 14).Clear();

        // Output all conversation dialogue lines to the "TempDialogue" spreadsheet.
        int namespaceIndex = 0;
        int rowIndex = 2;
        foreach (var sceneData in sceneDataList)
        {
            var categoryColor = GetCategoryColor(sceneData.Category);
            var nsColor = namespaceIndex++ % 2 == 1 ? NamespaceColorA : NamespaceColorB;

            var playerLineId = string.Empty;
            int sceneNoteIndex = 1;

            foreach (var dialogue in sceneData.DialogueComponents)
            {
                // playerLineId and sceneNoteIndex may be modified when we write a row
                WriteDialogueRow(
                    editedSheet, 
                    rowIndex, 
                    sceneData.
                    Category, 
                    sceneData.Namespace, 
                    categoryColor, 
                    nsColor, 
                    dialogue, 
                    ref playerLineId, 
                    ref sceneNoteIndex);

                rowIndex++;
            }
        }

        AppendBarkDialogue(dialogueSheet, editedSheet, rowIndex);
        FinalizeWorksheet(workbook, dialogueSheet, editedSheet);

        workbook.Save();

        return Result.Ok();
    }

    private void WriteDialogueRow(IXLWorksheet sheet, int row, string category, string ns, string categoryColor, string nsColor, IComponentProxy component, ref string playerLineId, ref int sceneNoteIndex)
    {
        sheet.Cell(row, 1).Value = category;
        sheet.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml(categoryColor);

        sheet.Cell(row, 2).Value = ns;
        sheet.Cell(row, 2).Style.Fill.BackgroundColor = XLColor.FromHtml(nsColor);

        if (component.Schema.ComponentType == LineEditor.ComponentType)
        {
            var characterId = component.GetString(LineEditor.CharacterId);
            var dialogueKey = component.GetString(LineEditor.DialogueKey);
            var lineId = dialogueKey[(dialogueKey.LastIndexOf('-') + 1)..];

            sheet.Cell(row, 3).Value = dialogueKey;
            sheet.Cell(row, 4).Value = characterId;

            if (characterId == "Player")
            {
                playerLineId = lineId;
                FillCells(sheet, row, new[] { 3, 4 }, PlayerColor);
            }
            else if (lineId == playerLineId)
            {
                FillCells(sheet, row, new[] { 3, 4 }, PlayerVariantColor);
            }
            else
            {
                playerLineId = string.Empty;
            }

            var sourceText = component.GetString(LineEditor.SourceText);
            if (sourceText.StartsWith("'") && !sourceText.StartsWith("''"))
            {
                sourceText = $"'{sourceText}";
            }

            sheet.Cell(row, 5).Value = component.GetString(LineEditor.SpeakingTo);
            sheet.Cell(row, 6).Value = sourceText;
            sheet.Cell(row, 7).Value = component.GetString(LineEditor.ContextNotes);
            sheet.Cell(row, 8).Value = component.GetString(LineEditor.Direction);
            sheet.Cell(row, 9).Value = component.GetString(LineEditor.GameArea);
            sheet.Cell(row, 10).Value = component.GetString(LineEditor.TimeConstraint);
            sheet.Cell(row, 11).Value = component.GetString(LineEditor.SoundProcessing);
            sheet.Cell(row, 12).Value = component.GetString(LineEditor.Platform);
            sheet.Cell(row, 13).Value = component.GetString(LineEditor.LinePriority);
            sheet.Cell(row, 14).Value = component.GetString(LineEditor.ProductionStatus);
        }
        else if (component.Schema.ComponentType == EntityConstants.EmptyComponentType)
        {
            var commentText = component.GetString("/comment");
            var noteKey = $"SceneNote-{ns}-Note{sceneNoteIndex++}";

            sheet.Cell(row, 3).Value = noteKey;
            sheet.Cell(row, 4).Value = "SceneNote";
            sheet.Cell(row, 6).Value = commentText;

            FillCells(sheet, row, Enumerable.Range(3, 12), SceneNoteColor);

            playerLineId = string.Empty;
        }
    }

    private void AppendBarkDialogue(IXLWorksheet originalSheet, IXLWorksheet targetSheet, int startRow)
    {
        int lastRow = originalSheet.LastRowUsed()?.RowNumber() ?? 1;
        for (int i = 2; i <= lastRow; i++)
        {
            if (originalSheet.Cell(i, 1).Value.ToString() == "Bark")
            {
                var barkRange = originalSheet.Range(i, 1, lastRow, 14);
                barkRange.CopyTo(targetSheet.Cell(startRow, 1));
                break;
            }
        }
    }

    private void FinalizeWorksheet(XLWorkbook workbook, IXLWorksheet originalSheet, IXLWorksheet editedSheet)
    {
        editedSheet.SheetView.TopLeftCellAddress = editedSheet.FirstCell().Address;
        editedSheet.SelectedRanges.RemoveAll();
        editedSheet.FirstCell().SetActive();

        originalSheet.Delete();
        editedSheet.Name = "Dialogue";
        editedSheet.Position = 1;
    }

    private void FillCells(IXLWorksheet sheet, int row, IEnumerable<int> columns, string hexColor)
    {
        foreach (var col in columns)
        {
            sheet.Cell(row, col).Style.Fill.BackgroundColor = XLColor.FromHtml(hexColor);
        }
    }

    private string GetCategoryColor(string category) => category switch
    {
        "Cinematic" => CinematicColor,
        "Conversation" => ConversationColor,
        "Bark" => BarkColor,
        _ => "FFFFFF"
    };
}
