using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class FileMultiEditCommand : CommandBase, IFileMultiEditCommand
{
    private readonly ILogger<FileMultiEditCommand> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public List<FileEditOperation> Edits { get; set; } = new();

    public FileMultiEditResult ResultValue { get; private set; } = new(0, Array.Empty<FileEditAffectedRange>());

    public FileMultiEditCommand(
        ILogger<FileMultiEditCommand> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (Edits.Count == 0)
        {
            ResultValue = new FileMultiEditResult(0, Array.Empty<FileEditAffectedRange>());
            return Result.Ok();
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

        var originalContent = await File.ReadAllTextAsync(resourcePath);
        var separator = LineEndingHelper.DetectSeparatorOrDefault(originalContent);

        // Sequential application: each edit anchors against the buffer state
        // produced by previous edits. We track previously-applied replacement
        // positions through subsequent edits so the final affected-line ranges
        // describe the post-batch document state, not mid-batch positions.
        var buffer = originalContent;
        var trackedPositions = new List<int>();
        var trackedNewStrings = new List<string>();

        for (var editIndex = 0; editIndex < Edits.Count; editIndex++)
        {
            var edit = Edits[editIndex];

            if (string.IsNullOrEmpty(edit.OldString))
            {
                return Result.Fail($"Edit {editIndex}: oldString must be non-empty");
            }

            var oldString = LineEndingHelper.ConvertLineEndings(edit.OldString, separator);
            var newString = LineEndingHelper.ConvertLineEndings(edit.NewString, separator);

            var matchPositions = FileEditMatching.FindAllMatches(buffer, oldString);

            if (matchPositions.Count == 0)
            {
                var quote = FileEditMatching.TruncateForQuote(oldString, 80);
                return Result.Fail($"Edit {editIndex}: oldString not found in file. Tried to match: '{quote}'");
            }

            if (matchPositions.Count > 1 && !edit.ReplaceAll)
            {
                return Result.Fail($"Edit {editIndex}: oldString matched {matchPositions.Count} occurrences; add surrounding context to disambiguate, or set replaceAll: true");
            }

            var applyResult = FileEditMatching.ApplyMatches(buffer, matchPositions, oldString, newString);

            UpdateTrackedPositions(trackedPositions, trackedNewStrings, matchPositions, oldString.Length, newString.Length);

            foreach (var pos in applyResult.ReplacementStarts)
            {
                trackedPositions.Add(pos);
                trackedNewStrings.Add(newString);
            }

            buffer = applyResult.NewContent;
        }

        var writeResult = await resourceService.FileWriter.WriteAllTextAsync(FileResource, buffer);
        if (writeResult.IsFailure)
        {
            return writeResult;
        }

        var affectedRanges = new List<FileEditAffectedRange>(trackedPositions.Count);
        for (var i = 0; i < trackedPositions.Count; i++)
        {
            var position = trackedPositions[i];
            if (position < 0)
            {
                continue;
            }
            affectedRanges.Add(FileEditMatching.RangeForReplacement(buffer, position, trackedNewStrings[i]));
        }
        affectedRanges.Sort((a, b) => a.FromLine.CompareTo(b.FromLine));

        ResultValue = new FileMultiEditResult(Edits.Count, affectedRanges);

        return Result.Ok();
    }

    /// <summary>
    /// Shifts each previously-tracked replacement position to its location in
    /// the buffer that will exist after the current edit's matches are applied.
    /// A tracked position that lies inside one of the current matches has been
    /// overwritten by the current edit and is marked as -1 so it drops out of
    /// the final affected-line list.
    /// </summary>
    private static void UpdateTrackedPositions(
        List<int> trackedPositions,
        List<string> trackedNewStrings,
        List<int> currentMatches,
        int oldLen,
        int newLen)
    {
        var delta = newLen - oldLen;
        for (var i = 0; i < trackedPositions.Count; i++)
        {
            var p = trackedPositions[i];
            if (p < 0)
            {
                continue;
            }

            var shift = 0;
            var invalidated = false;
            foreach (var m in currentMatches)
            {
                if (p < m)
                {
                    break;
                }
                if (p < m + oldLen)
                {
                    invalidated = true;
                    break;
                }
                shift += delta;
            }

            if (invalidated)
            {
                trackedPositions[i] = -1;
            }
            else
            {
                trackedPositions[i] = p + shift;
            }
        }
    }
}
