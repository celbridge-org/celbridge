using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Documents.Commands;

public class ApplyEditsCommand : CommandBase, IApplyEditsCommand
{
    private readonly ILogger<ApplyEditsCommand> _logger;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IDialogService _dialogService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ICommandService _commandService;

    public List<DocumentEdit> Edits { get; set; } = new();
    public bool OpenDocument { get; set; } = true;

    public ApplyEditsCommand(
        ILogger<ApplyEditsCommand> logger,
        IStringLocalizer stringLocalizer,
        IDialogService dialogService,
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService)
    {
        _logger = logger;
        _stringLocalizer = stringLocalizer;
        _dialogService = dialogService;
        _workspaceWrapper = workspaceWrapper;
        _commandService = commandService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (Edits.Count == 0)
        {
            return Result.Ok();
        }

        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;
        var documentsPanel = _workspaceWrapper.WorkspaceService.DocumentsPanel;
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var failedResources = new List<ResourceKey>();

        foreach (var documentEdit in Edits)
        {
            var resource = documentEdit.Resource;

            // Check if document is already open
            var documentView = documentsPanel.GetDocumentView(resource);

            if (documentView is not null)
            {
                // Document is already open, always route through the editor.
                // Resolve any EndColumn == -1 sentinel to int.MaxValue: Monaco clamps
                // out-of-range columns to the actual line end, so this reliably means
                // "replace to end of line" without knowing the exact character count.
                var resolvedEdits = ResolveEndOfLineColumns(documentEdit.Edits);
                var applyResult = await documentView.ApplyEditsAsync(resolvedEdits);
                if (applyResult.IsFailure)
                {
                    _logger.LogWarning($"Failed to apply edits to document: {resource}");
                    failedResources.Add(resource);
                }
            }
            else if (OpenDocument)
            {
                // Open the document and apply edits through the editor
                var openResult = await documentsService.OpenDocument(resource, forceReload: false, location: string.Empty, activate: false);
                if (openResult.IsFailure)
                {
                    _logger.LogWarning($"Failed to open document for applying edits: {resource}");
                    failedResources.Add(resource);
                    continue;
                }

                documentView = documentsPanel.GetDocumentView(resource);
                if (documentView is null)
                {
                    _logger.LogWarning($"Document view not found after opening: {resource}");
                    failedResources.Add(resource);
                    continue;
                }

                var resolvedEditsForOpen = ResolveEndOfLineColumns(documentEdit.Edits);
                var applyResult = await documentView.ApplyEditsAsync(resolvedEditsForOpen);
                if (applyResult.IsFailure)
                {
                    _logger.LogWarning($"Failed to apply edits to document: {resource}");
                    failedResources.Add(resource);
                }
            }
            else
            {
                // Apply edits directly to the file on disk
                var applyResult = await ApplyEditsToDisk(resourceRegistry, resource, documentEdit.Edits);
                if (applyResult.IsFailure)
                {
                    _logger.LogWarning($"Failed to apply edits to file on disk: {resource}");
                    failedResources.Add(resource);
                }
            }
        }

        if (failedResources.Count > 0)
        {
            var errorMessage = $"Failed to apply edits to the following documents: {string.Join(", ", failedResources)}";
            _logger.LogError(errorMessage);

            var alertTitle = _stringLocalizer.GetString("Documents_ApplyEditsFailedTitle");
            string alertMessage;
            if (failedResources.Count == 1)
            {
                var failedFile = failedResources[0].ToString();
                alertMessage = _stringLocalizer.GetString("Documents_ApplyEditsFailedSingle", failedFile);
            }
            else
            {
                alertMessage = _stringLocalizer.GetString("Documents_ApplyEditsFailedMultiple", failedResources.Count);
            }

            // Fire-and-forget to avoid blocking
            _ = _dialogService.ShowAlertDialogAsync(alertTitle, alertMessage);

            return Result.Fail(errorMessage);
        }

        return Result.Ok();
    }

    private static async Task<Result> ApplyEditsToDisk(IResourceRegistry resourceRegistry, ResourceKey resource, List<TextEdit> edits)
    {
        var resolveResult = resourceRegistry.ResolveResourcePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        if (!File.Exists(resourcePath))
        {
            return Result.Fail($"File not found: '{resource}'");
        }

        var lines = new List<string>(await File.ReadAllLinesAsync(resourcePath));

        // Sort edits in reverse order (bottom-to-top, right-to-left) so earlier edits
        // don't shift the positions of later edits
        var sortedEdits = edits
            .OrderByDescending(e => e.Line)
            .ThenByDescending(e => e.Column)
            .ToList();

        foreach (var edit in sortedEdits)
        {
            // Convert from 1-based to 0-based indices
            var startLine = edit.Line - 1;
            var startColumn = edit.Column - 1;
            var endLine = edit.EndLine - 1;

            // EndColumn of -1 is a sentinel meaning "end of line": no text is preserved
            // after the edit range on the end line.
            var endColumn = edit.EndColumn == -1
                ? lines[endLine].Length
                : edit.EndColumn - 1;

            if (startLine < 0 || startLine >= lines.Count)
            {
                return Result.Fail($"Edit start line {edit.Line} is out of range (file has {lines.Count} lines)");
            }

            if (endLine < 0 || endLine >= lines.Count)
            {
                return Result.Fail($"Edit end line {edit.EndLine} is out of range (file has {lines.Count} lines)");
            }

            // Build the text before the edit range
            var beforeEdit = lines[startLine].Substring(0, Math.Min(startColumn, lines[startLine].Length));

            // Build the text after the edit range
            var afterEdit = endColumn <= lines[endLine].Length
                ? lines[endLine].Substring(endColumn)
                : string.Empty;

            // Combine: before + new text + after
            var newContent = beforeEdit + edit.NewText + afterEdit;
            var newLines = newContent.Split('\n');

            // Remove the original lines in the edit range and insert the new lines
            var lineCount = endLine - startLine + 1;
            lines.RemoveRange(startLine, lineCount);
            lines.InsertRange(startLine, newLines);
        }

        await File.WriteAllLinesAsync(resourcePath, lines);

        return Result.Ok();
    }

    private static List<TextEdit> ResolveEndOfLineColumns(List<TextEdit> edits)
    {
        var hasEndOfLineSentinel = edits.Any(e => e.EndColumn == -1);
        if (!hasEndOfLineSentinel)
        {
            return edits;
        }

        return edits
            .Select(edit => edit.EndColumn == -1
                ? edit with { EndColumn = int.MaxValue }
                : edit)
            .ToList();
    }

    //
    // Static methods for scripting support.
    //

    public static void ApplyEdits(List<DocumentEdit> edits)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IApplyEditsCommand>(command =>
        {
            command.Edits = edits;
        });
    }
}
