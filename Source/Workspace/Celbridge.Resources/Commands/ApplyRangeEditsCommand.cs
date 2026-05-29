using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Resources.Commands;

public class ApplyRangeEditsCommand : CommandBase, IApplyRangeEditsCommand
{
    private readonly ILogger<ApplyRangeEditsCommand> _logger;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IDialogService _dialogService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public List<FileRangeEdit> Edits { get; set; } = new();

    public ApplyRangeEditsCommand(
        ILogger<ApplyRangeEditsCommand> logger,
        IStringLocalizer stringLocalizer,
        IDialogService dialogService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _stringLocalizer = stringLocalizer;
        _dialogService = dialogService;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (Edits.Count == 0)
        {
            return Result.Ok();
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ResourceService.Registry;
        var fileStorage = workspaceService.FileStorage;

        var failedResources = new List<ResourceKey>();
        var failureDetails = new List<string>();

        foreach (var fileEdit in Edits)
        {
            var resource = fileEdit.Resource;

            var applyResult = await ApplyEditsToDisk(resourceRegistry, fileStorage, resource, fileEdit.Edits);
            if (applyResult.IsFailure)
            {
                _logger.LogWarning($"Failed to apply edits to file on disk: {resource}");
                failedResources.Add(resource);
                failureDetails.Add($"{resource}: {applyResult.FirstErrorMessage}");
            }
        }

        if (failedResources.Count > 0)
        {
            // The headline names every failed resource. The detail block below
            // names each failure's reason so the agent does not have to retry
            // and read a separate log line to learn what the validator caught.
            var headline = $"Failed to apply edits to: {string.Join(", ", failedResources)}";
            var detail = string.Join(Environment.NewLine, failureDetails);
            var errorMessage = $"{headline}{Environment.NewLine}{detail}";
            _logger.LogError(errorMessage);

            var alertTitle = _stringLocalizer.GetString("Documents_ApplyRangeEditsFailedTitle");
            string alertMessage;
            if (failedResources.Count == 1)
            {
                var failedFile = failedResources[0].ToString();
                alertMessage = _stringLocalizer.GetString("Documents_ApplyRangeEditsFailedSingle", failedFile);
            }
            else
            {
                alertMessage = _stringLocalizer.GetString("Documents_ApplyRangeEditsFailedMultiple", failedResources.Count);
            }

            // Fire-and-forget to avoid blocking
            _ = _dialogService.ShowAlertDialogAsync(alertTitle, alertMessage);

            return Result.Fail(errorMessage);
        }

        return Result.Ok();
    }

    private static async Task<Result> ApplyEditsToDisk(
        IResourceRegistry resourceRegistry,
        IFileStorage fileStorage,
        ResourceKey resource,
        List<RangeEdit> edits)
    {
        var infoResult = await fileStorage.GetInfoAsync(resource);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != StorageItemKind.File)
        {
            return Result.Fail($"File not found: '{resource}'");
        }

        // Read the file's existing content to capture its line-ending style and
        // trailing-newline state. Both must be preserved across the edit so the
        // file's on-disk format does not silently drift.
        var readResult = await fileStorage.ReadAllTextAsync(resource);
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read file: '{resource}'")
                .WithErrors(readResult);
        }
        var originalContent = readResult.Value;
        var originalSeparator = LineEndingHelper.DetectSeparatorOrDefault(originalContent);
        var originalEndsWithNewline = LineEndingHelper.EndsWithNewline(originalContent);

        // The line list models content only. A terminating newline is re-added
        // at write time based on originalEndsWithNewline.
        var lines = LineEndingHelper.SplitToContentLines(originalContent);

        // Sort edits in reverse order (bottom-to-top, right-to-left) so earlier edits
        // don't shift the positions of later edits as we apply them.
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

            if (startLine < 0 || startLine >= lines.Count)
            {
                return Result.Fail($"Edit start line {edit.Line} is out of range (file has {lines.Count} lines)");
            }

            if (endLine < 0 || endLine >= lines.Count)
            {
                return Result.Fail($"Edit end line {edit.EndLine} is out of range (file has {lines.Count} lines)");
            }

            // EndColumn of -1 is a sentinel meaning "end of line": no text is preserved
            // after the edit range on the end line.
            var endColumn = edit.EndColumn == -1
                ? lines[endLine].Length
                : edit.EndColumn - 1;

            // Build the text before the edit range
            var beforeEdit = lines[startLine].Substring(0, Math.Min(startColumn, lines[startLine].Length));

            // Build the text after the edit range
            var afterEdit = endColumn <= lines[endLine].Length
                ? lines[endLine].Substring(endColumn)
                : string.Empty;

            // Normalise NewText to a lone-\n separator so splitting on \n yields
            // clean content-only lines that can be joined back with the file's
            // existing separator without producing \r\r\n sequences. Same idiom
            // as WriteFileCommand and ReplaceFileCommand.
            var normalisedNewText = LineEndingHelper.ConvertLineEndings(edit.NewText, "\n");
            var newContent = beforeEdit + normalisedNewText + afterEdit;
            var newLines = newContent.Split('\n');

            // Remove the original lines in the edit range and insert the new lines
            var lineCount = endLine - startLine + 1;
            lines.RemoveRange(startLine, lineCount);
            lines.InsertRange(startLine, newLines);
        }

        var output = string.Join(originalSeparator, lines);
        if (originalEndsWithNewline && output.Length > 0)
        {
            output += originalSeparator;
        }

        var writeResult = await fileStorage.WriteAllTextAsync(resource, output);
        if (writeResult.IsFailure)
        {
            return Result.Fail($"Failed to write edits to file: '{resource}'")
                .WithErrors(writeResult);
        }

        return Result.Ok();
    }
}
