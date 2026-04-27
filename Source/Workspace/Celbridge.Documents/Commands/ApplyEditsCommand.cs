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

    public List<DocumentEdit> Edits { get; set; } = new();

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

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var failedResources = new List<ResourceKey>();

        foreach (var documentEdit in Edits)
        {
            var resource = documentEdit.Resource;

            var applyResult = await ApplyEditsToDisk(resourceRegistry, resource, documentEdit.Edits);
            if (applyResult.IsFailure)
            {
                _logger.LogWarning($"Failed to apply edits to file on disk: {resource}");
                failedResources.Add(resource);
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
