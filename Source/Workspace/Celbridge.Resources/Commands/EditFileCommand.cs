using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class EditFileCommand : CommandBase, IEditFileCommand
{
    private readonly ILogger<EditFileCommand> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string OldString { get; set; } = string.Empty;
    public string NewString { get; set; } = string.Empty;
    public bool ReplaceAll { get; set; }

    public EditFileResult ResultValue { get; private set; } = new(0, Array.Empty<FileEditAffectedRange>(), false);

    public EditFileCommand(
        ILogger<EditFileCommand> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (string.IsNullOrEmpty(OldString))
        {
            return Result.Fail("oldString must be non-empty. To append to a file, anchor on the existing last line and concatenate the new content in newString. To overwrite or create a file, use file_write.");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var fileSystem = workspaceService.ResourceFileSystem;

        var infoResult = await fileSystem.GetInfoAsync(FileResource);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != ResourceInfoKind.File)
        {
            return Result.Fail($"File not found: '{FileResource}'");
        }

        var readResult = await fileSystem.ReadAllTextAsync(FileResource);
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read file: '{FileResource}'")
                .WithErrors(readResult);
        }
        var content = readResult.Value;
        var separator = LineEndingHelper.DetectSeparatorOrDefault(content);
        var oldString = LineEndingHelper.ConvertLineEndings(OldString, separator);
        var newString = LineEndingHelper.ConvertLineEndings(NewString, separator);

        var matchPositions = FileEditMatching.FindAllMatches(content, oldString);

        if (matchPositions.Count == 0)
        {
            var quote = FileEditMatching.TruncateForQuote(oldString, 80);
            return Result.Fail($"oldString not found in file. Tried to match: '{quote}'");
        }

        if (matchPositions.Count > 1 && !ReplaceAll)
        {
            return Result.Fail($"oldString matched {matchPositions.Count} occurrences; add surrounding context to disambiguate, or set replaceAll: true");
        }

        var buildResult = FileEditMatching.ApplyMatches(content, matchPositions, oldString, newString);
        var newContent = buildResult.NewContent;
        var replacementStarts = buildResult.ReplacementStarts;

        var writeResult = await fileSystem.WriteAllTextAsync(FileResource, newContent);
        if (writeResult.IsFailure)
        {
            return writeResult;
        }

        var affectedRanges = new List<FileEditAffectedRange>(replacementStarts.Count);
        foreach (var start in replacementStarts)
        {
            affectedRanges.Add(FileEditMatching.RangeForReplacement(newContent, start, newString));
        }

        var mergedRanges = FileEditMatching.MergeSameLineRanges(affectedRanges);
        var capped = FileEditMatching.CapVerboseRanges(mergedRanges);

        ResultValue = new EditFileResult(matchPositions.Count, capped.Ranges, capped.Truncated);

        return Result.Ok();
    }
}
