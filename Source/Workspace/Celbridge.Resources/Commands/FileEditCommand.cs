using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class FileEditCommand : CommandBase, IFileEditCommand
{
    private readonly ILogger<FileEditCommand> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string OldString { get; set; } = string.Empty;
    public string NewString { get; set; } = string.Empty;
    public bool ReplaceAll { get; set; }

    public FileEditResult ResultValue { get; private set; } = new(0, Array.Empty<FileEditAffectedRange>(), false);

    public FileEditCommand(
        ILogger<FileEditCommand> logger,
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

        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;

        var resolveResult = resourceService.Registry.ResolveResourcePath(FileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{FileResource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        if (!File.Exists(resourcePath))
        {
            return Result.Fail($"File not found: '{FileResource}'");
        }

        var content = await File.ReadAllTextAsync(resourcePath);
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

        var writeResult = await resourceService.FileWriter.WriteAllTextAsync(FileResource, newContent);
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

        ResultValue = new FileEditResult(matchPositions.Count, capped.Ranges, capped.Truncated);

        return Result.Ok();
    }
}
