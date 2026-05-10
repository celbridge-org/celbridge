using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Resources.Commands;

public class ApplyEditsCommand : CommandBase, IApplyEditsCommand
{
    private readonly ILogger<ApplyEditsCommand> _logger;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IDialogService _dialogService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public List<FileEdit> Edits { get; set; } = new();

    public IReadOnlyList<AppliedEdit> ResultValue { get; private set; } = Array.Empty<AppliedEdit>();

    public ApplyEditsCommand(
        ILogger<ApplyEditsCommand> logger,
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

        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;

        var failedResources = new List<ResourceKey>();
        var failureDetails = new List<string>();
        var appliedRanges = new List<AppliedEdit>();

        foreach (var fileEdit in Edits)
        {
            var resource = fileEdit.Resource;

            var applyResult = await ApplyEditsToDisk(resourceService, resource, fileEdit.Edits);
            if (applyResult.IsFailure)
            {
                _logger.LogWarning($"Failed to apply edits to file on disk: {resource}");
                failedResources.Add(resource);
                failureDetails.Add($"{resource}: {applyResult.FirstErrorMessage}");
            }
            else
            {
                appliedRanges.AddRange(applyResult.Value);
            }
        }

        if (failedResources.Count > 0)
        {
            // The headline names every failed resource; the detail block below
            // names each failure's reason so the agent does not have to retry
            // and read a separate log line to learn what the validator caught.
            var headline = $"Failed to apply edits to: {string.Join(", ", failedResources)}";
            var detail = string.Join(Environment.NewLine, failureDetails);
            var errorMessage = $"{headline}{Environment.NewLine}{detail}";
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

        ResultValue = appliedRanges;
        return Result.Ok();
    }

    private static async Task<Result<IReadOnlyList<AppliedEdit>>> ApplyEditsToDisk(IResourceService resourceService, ResourceKey resource, List<TextEdit> edits)
    {
        var resourceRegistry = resourceService.Registry;

        var resolveResult = resourceRegistry.ResolveResourcePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result<IReadOnlyList<AppliedEdit>>.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        if (!File.Exists(resourcePath))
        {
            return Result<IReadOnlyList<AppliedEdit>>.Fail($"File not found: '{resource}'");
        }

        // Read the file's existing content to capture its line-ending style and
        // trailing-newline state. Both must be preserved across the edit so the
        // file's on-disk format does not silently drift.
        var originalContent = await File.ReadAllTextAsync(resourcePath);
        var originalSeparator = LineEndingHelper.DetectSeparatorOrDefault(originalContent);
        var originalEndsWithNewline = LineEndingHelper.EndsWithNewline(originalContent);

        // The line list models content only. A terminating newline is re-added
        // at write time based on originalEndsWithNewline.
        var lines = LineEndingHelper.SplitToContentLines(originalContent);

        // Capture the original file line count before any edits land. Append
        // edits (line == endLine == -1) compute their post-edit ranges from
        // this anchor plus the cumulative line delta of earlier edits.
        var originalFileLineCount = lines.Count;

        // Sort edits in reverse order (bottom-to-top, right-to-left) so earlier edits
        // don't shift the positions of later edits as we apply them. Append edits
        // (Line == -1) sort last and run after every in-range edit; since they only
        // grow the tail of the file, ordering relative to other appends does not matter.
        var sortedEdits = edits
            .OrderByDescending(e => e.Line)
            .ThenByDescending(e => e.Column)
            .ToList();

        // Track the post-edit line count produced by each edit. Pairing the edit
        // with its newLineCount lets us derive each edit's post-edit line range
        // by walking the edits in original (forward) order and accumulating the
        // line-count delta from earlier edits.
        var applied = new List<(TextEdit Edit, int NewLineCount)>(sortedEdits.Count);

        foreach (var edit in sortedEdits)
        {
            // -1 in both Line and EndLine is the append sentinel: insert NewText
            // (split into lines) after the current last line. No existing content
            // is replaced.
            if (edit.Line == -1 && edit.EndLine == -1)
            {
                var appendedLines = edit.NewText.Split('\n');
                lines.AddRange(appendedLines);
                applied.Add((edit, appendedLines.Length));
                continue;
            }

            if (edit.Line == -1 || edit.EndLine == -1)
            {
                return Result<IReadOnlyList<AppliedEdit>>.Fail("Append sentinel requires both 'line' and 'endLine' to be -1");
            }

            // Convert from 1-based to 0-based indices
            var startLine = edit.Line - 1;
            var startColumn = edit.Column - 1;
            var endLine = edit.EndLine - 1;

            if (startLine < 0 || startLine >= lines.Count)
            {
                return Result<IReadOnlyList<AppliedEdit>>.Fail($"Edit start line {edit.Line} is out of range (file has {lines.Count} lines)");
            }

            if (endLine < 0 || endLine >= lines.Count)
            {
                return Result<IReadOnlyList<AppliedEdit>>.Fail($"Edit end line {edit.EndLine} is out of range (file has {lines.Count} lines)");
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

            // Combine: before + new text + after. NewText from MCP callers uses
            // \n separators; splitting on \n produces the new logical lines.
            var newContent = beforeEdit + edit.NewText + afterEdit;
            var newLines = newContent.Split('\n');

            // Remove the original lines in the edit range and insert the new lines
            var lineCount = endLine - startLine + 1;
            lines.RemoveRange(startLine, lineCount);
            lines.InsertRange(startLine, newLines);

            applied.Add((edit, newLines.Length));
        }

        var output = string.Join(originalSeparator, lines);
        if (originalEndsWithNewline && output.Length > 0)
        {
            output += originalSeparator;
        }

        var writeResult = await resourceService.FileWriter.WriteAllTextAsync(resource, output);
        if (writeResult.IsFailure)
        {
            return Result<IReadOnlyList<AppliedEdit>>.Fail($"Failed to write edits to file: '{resource}'")
                .WithErrors(writeResult);
        }

        // Walk the applied edits in original (forward) order and accumulate
        // the line-count delta to compute each edit's post-edit range. Append
        // edits use originalFileLineCount + 1 as their effective source line
        // so they sort after every in-range edit and pick up the full
        // accumulated delta.
        var ranges = new List<AppliedEdit>(applied.Count);
        var cumulativeDelta = 0;
        var sortedApplied = applied
            .OrderBy(p => p.Edit.Line == -1 ? originalFileLineCount + 1 : p.Edit.Line)
            .ThenBy(p => p.Edit.Column);
        foreach (var pair in sortedApplied)
        {
            int originalLineCount;
            int sourceLine;
            if (pair.Edit.Line == -1)
            {
                originalLineCount = 0;
                sourceLine = originalFileLineCount + 1;
            }
            else
            {
                originalLineCount = pair.Edit.EndLine - pair.Edit.Line + 1;
                sourceLine = pair.Edit.Line;
            }

            var postEditStart = sourceLine + cumulativeDelta;
            var postEditEnd = postEditStart + pair.NewLineCount - 1;
            ranges.Add(new AppliedEdit(resource, postEditStart, postEditEnd));
            cumulativeDelta += pair.NewLineCount - originalLineCount;
        }

        return Result<IReadOnlyList<AppliedEdit>>.Ok(ranges);
    }

    //
    // Static methods for scripting support.
    //

    public static void ApplyEdits(List<FileEdit> edits)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IApplyEditsCommand>(command =>
        {
            command.Edits = edits;
        });
    }
}
