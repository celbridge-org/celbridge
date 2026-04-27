using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.Resources;
using Celbridge.Utilities;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Documents.Commands;

public class ApplyEditsCommand : CommandBase, IApplyEditsCommand
{
    private readonly ILogger<ApplyEditsCommand> _logger;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IDialogService _dialogService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public List<DocumentEdit> Edits { get; set; } = new();

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
        var appliedRanges = new List<AppliedEdit>();

        foreach (var documentEdit in Edits)
        {
            var resource = documentEdit.Resource;

            var applyResult = await ApplyEditsToDisk(resourceService, resource, documentEdit.Edits);
            if (applyResult.IsFailure)
            {
                _logger.LogWarning($"Failed to apply edits to file on disk: {resource}");
                failedResources.Add(resource);
            }
            else
            {
                appliedRanges.AddRange(applyResult.Value);
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

        // Sort edits in reverse order (bottom-to-top, right-to-left) so earlier edits
        // don't shift the positions of later edits as we apply them.
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
            return Result<IReadOnlyList<AppliedEdit>>.Fail(writeResult.FirstErrorMessage)
                .WithErrors(writeResult);
        }

        // Walk the applied edits in original (forward) order and accumulate
        // the line-count delta to compute each edit's post-edit range.
        var ranges = new List<AppliedEdit>(applied.Count);
        var cumulativeDelta = 0;
        foreach (var pair in applied.OrderBy(p => p.Edit.Line).ThenBy(p => p.Edit.Column))
        {
            var originalLineCount = pair.Edit.EndLine - pair.Edit.Line + 1;
            var postEditStart = pair.Edit.Line + cumulativeDelta;
            var postEditEnd = postEditStart + pair.NewLineCount - 1;
            ranges.Add(new AppliedEdit(resource, postEditStart, postEditEnd));
            cumulativeDelta += pair.NewLineCount - originalLineCount;
        }

        return Result<IReadOnlyList<AppliedEdit>>.Ok(ranges);
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
