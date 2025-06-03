using Celbridge.Dialog;
using Celbridge.Entities;
using Celbridge.Explorer;
using Celbridge.Messaging;
using Celbridge.Screenplay.Components;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Screenplay.Services;

public class ScreenplaySaver
{
    // Stores the properties from a Player line so that they can be propogated to the 
    // following PlayerVariant lines.
    private record PlayerLine()
    {
        public string LineId { get; init; } = string.Empty;
        public string SpeakingTo { get; init; } = string.Empty;
        public string ContextNotes { get; init; } = string.Empty;
        public string Direction { get; init; } = string.Empty;
        public string GameArea { get; init; } = string.Empty;
        public string TimeConstraint { get; init; } = string.Empty;
        public string SoundProcessing { get; init; } = string.Empty;
        public string Platform { get; init; } = string.Empty;
        public string LinePriority { get; init; } = string.Empty;
        public string ProductionStatus { get; init; } = string.Empty;
    };

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

            var errorScene = ResourceKey.Empty;

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
                    errorScene = sceneResource;
                    break;
                }
            }

            if (!errorScene.IsEmpty)
            {
                // Broadcast a message to notify the user about the save error.
                var message = new SaveScreenplayFailedMessage(errorScene);
                _messengerService.Send(message);

                // The save operation is considered to have succeeded if a scene error was detected and handled correctly.
                return Result.Ok();
            }

            var saveWorksheetResult = SaveDialogueWorksheet(workbookPath, sceneDataList);

            if (saveWorksheetResult.IsSuccess)
            {
                // Broadcast a message to notify the user about the successful save.
                var message = new SaveScreenplaySucceededMessage();
                _messengerService.Send(message);
            }

            // Todo: Handle errors in the save operation (e.g. permissions)

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
            if (!sceneComponent.IsComponentType(SceneEditor.ComponentType))
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
                .Where(c => c.IsComponentType(LineEditor.ComponentType))
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

            PlayerLine? playerLine = null;

            int sceneNoteIndex = 1;

            foreach (var dialogue in sceneData.DialogueComponents)
            {
                // playerLineId and sceneNoteIndex may be modified when we write a row
                var writeResult = WriteDialogueRow(
                    editedSheet, 
                    rowIndex, 
                    sceneData.
                    Category, 
                    sceneData.Namespace, 
                    categoryColor, 
                    nsColor, 
                    dialogue, 
                    ref playerLine, // Use the returned player line (if any) on the next iteration
                    ref sceneNoteIndex);

                if (writeResult.IsFailure)
                {
                    return Result.Fail($"Failed to write dialogue row {rowIndex}");
                }

                rowIndex++;
            }
        }

        AppendBarkDialogue(dialogueSheet, editedSheet, rowIndex);
        FinalizeWorksheet(workbook, dialogueSheet, editedSheet);

        workbook.Save();

        return Result.Ok();
    }

    private Result WriteDialogueRow(IXLWorksheet sheet, int row, string category, string ns, string categoryColor, string nsColor, IComponentProxy component, ref PlayerLine? playerLine, ref int sceneNoteIndex)
    {
        sheet.Cell(row, 1).Value = category;
        sheet.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml(categoryColor);

        sheet.Cell(row, 2).Value = ns;
        sheet.Cell(row, 2).Style.Fill.BackgroundColor = XLColor.FromHtml(nsColor);

        if (component.IsComponentType(LineEditor.ComponentType))
        {
            //
            // Acquire line properties from the Line component
            //

            var lineType = component.GetString(LineEditor.LineType);
            var lineId = component.GetString(LineEditor.LineId);
            var characterId = component.GetString(LineEditor.CharacterId);
            var sourceText = component.GetString(LineEditor.SourceText);
            var speakingTo = component.GetString(LineEditor.SpeakingTo);
            var contextNotes = component.GetString(LineEditor.ContextNotes);
            var direction = component.GetString(LineEditor.Direction);
            var gameArea = component.GetString(LineEditor.GameArea);
            var timeConstraint = component.GetString(LineEditor.TimeConstraint);
            var soundProcessing = component.GetString(LineEditor.SoundProcessing);
            var platform = component.GetString(LineEditor.Platform);
            var linePriority = component.GetString(LineEditor.LinePriority);
            var productionStatus = component.GetString(LineEditor.ProductionStatus);

            var dialogueKey = $"{characterId}-{ns}-{lineId}";

            var isSceneNote = false;

            // Excel uses a single apostrophe to indicate raw text.
            // This causes problems if the first word in a sentence is a contraction, e.g. 'Fraid so.
            // Todo: Should we do this for all free text entry fields?
            if (sourceText.StartsWith("'") && !sourceText.StartsWith("''"))
            {
                // Escape single leading apostrophes by replacing with double apostrophes.
                sourceText = $"'{sourceText}";
            }

            //
            // Handle different Line Types
            //

            if (lineType == "Player")
            {
                // Start a new player line
                // Record the properties to be copied for player variant lines
                playerLine = new PlayerLine()
                {
                    LineId = lineId,
                    SpeakingTo = speakingTo,
                    ContextNotes = contextNotes,
                    Direction = direction,
                    GameArea = gameArea,
                    TimeConstraint = timeConstraint,
                    SoundProcessing = soundProcessing,
                    Platform = platform,
                    LinePriority = linePriority,
                    ProductionStatus = productionStatus
                };

                FillCells(sheet, row, new[] { 3, 4 }, PlayerColor);
            }
            else if (lineType == "PlayerVariant")
            {
                Guard.IsNotNull(playerLine);
                    
                // For Player Variant lines these fields should all match the parent Player Line
                lineId = playerLine.LineId;
                speakingTo = playerLine.SpeakingTo;
                // contextNotes = playerLine.ContextNotes;
                gameArea = playerLine.GameArea;
                timeConstraint = playerLine.TimeConstraint;
                soundProcessing = playerLine.SoundProcessing;
                platform = playerLine.Platform;
                linePriority = playerLine.LinePriority;
                productionStatus = playerLine.ProductionStatus;

                //if (string.IsNullOrEmpty(direction))
                //{
                //    // Use the direction from the Player ine if no direction is specified for the PlayerVariant
                //    direction = playerLine.Direction;
                //}

                // Override the dialogue key for Player Variants
                dialogueKey = $"{characterId}-{ns}-{lineId}";

                FillCells(sheet, row, new[] { 3, 4 }, PlayerVariantColor);
            }
            else if (lineType == "SceneNote")
            {
                playerLine = null;

                isSceneNote = true;
                characterId = "SceneNote";

                // Override the dialogue key for scene notes
                // Todo: Do we still need to do this? Could we just treat them as regular dialogue lines now?
                dialogueKey = $"SceneNote-{ns}-Note{sceneNoteIndex++}";

                FillCells(sheet, row, Enumerable.Range(3, 12), SceneNoteColor);
            }
            else if (lineType == "NPC")
            {
                playerLine = null;
            }
            else
            {
                return Result.Fail($"Invalid line type '{lineType}'");
            }

            //
            // Populate the spreadsheet
            //

            sheet.Cell(row, 3).Value = dialogueKey;
            sheet.Cell(row, 4).Value = characterId;
            sheet.Cell(row, 6).Value = sourceText;

            if (!isSceneNote)
            {
                sheet.Cell(row, 5).Value = speakingTo;
                sheet.Cell(row, 7).Value = contextNotes;
                sheet.Cell(row, 8).Value = direction;
                sheet.Cell(row, 9).Value = gameArea;
                sheet.Cell(row, 10).Value = timeConstraint;
                sheet.Cell(row, 11).Value = soundProcessing;
                sheet.Cell(row, 12).Value = platform;
                sheet.Cell(row, 13).Value = linePriority;
                sheet.Cell(row, 14).Value = productionStatus;
            }
        }

        return Result.Ok();
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
