using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class MultiEditFileCommand : CommandBase, IMultiEditFileCommand
{
    private readonly ILogger<MultiEditFileCommand> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public List<FileEditOperation> Edits { get; set; } = new();

    public MultiEditFileResult ResultValue { get; private set; } = new(
        0,
        Array.Empty<MultiEditFileEditSummary>(),
        Array.Empty<MultiEditFileAffectedRange>());

    public MultiEditFileCommand(
        ILogger<MultiEditFileCommand> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (Edits.Count == 0)
        {
            ResultValue = new MultiEditFileResult(
                0,
                Array.Empty<MultiEditFileEditSummary>(),
                Array.Empty<MultiEditFileAffectedRange>());
            return Result.Ok();
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceFileSystem = workspaceService.ResourceFileSystem;

        var infoResult = await resourceFileSystem.GetInfoAsync(FileResource);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != StorageItemKind.File)
        {
            return Result.Fail($"File not found: '{FileResource}'")
                .WithErrors(infoResult);
        }

        var readResult = await resourceFileSystem.ReadAllTextAsync(FileResource);
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read file: '{FileResource}'")
                .WithErrors(readResult);
        }
        var originalContent = readResult.Value;
        var separator = LineEndingHelper.DetectSeparatorOrDefault(originalContent);

        // Sequential application: each edit anchors against the buffer state
        // produced by previous edits. Per-edit tracked positions let us cap
        // each edit's range list independently and tag every surviving range
        // with the originating edit index in the final response.
        var buffer = originalContent;
        var trackedPositions = new List<int>();
        var trackedNewStrings = new List<string>();
        var trackedEditIndices = new List<int>();
        var perEditMatchCount = new int[Edits.Count];

        for (var editIndex = 0; editIndex < Edits.Count; editIndex++)
        {
            var edit = Edits[editIndex];

            if (string.IsNullOrEmpty(edit.OldString))
            {
                return Result.Fail($"Edit {editIndex}: oldString must be non-empty. To append to a file, anchor on the existing last line and concatenate the new content in newString. To overwrite or create a file, use file_write.");
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

            perEditMatchCount[editIndex] = matchPositions.Count;

            var applyResult = FileEditMatching.ApplyMatches(buffer, matchPositions, oldString, newString);

            UpdateTrackedPositions(trackedPositions, matchPositions, oldString.Length, newString.Length);

            foreach (var pos in applyResult.ReplacementStarts)
            {
                trackedPositions.Add(pos);
                trackedNewStrings.Add(newString);
                trackedEditIndices.Add(editIndex);
            }

            buffer = applyResult.NewContent;
        }

        var writeResult = await resourceFileSystem.WriteAllTextAsync(FileResource, buffer);
        if (writeResult.IsFailure)
        {
            return writeResult;
        }

        // Group surviving positions by their originating edit, then build the
        // final ranges per edit so the verbose cap applies per-edit rather
        // than across the whole batch. An edit whose matches were all
        // overwritten by a later edit still gets a summary entry with its
        // original match count.
        var positionsByEdit = new List<int>[Edits.Count];
        var newStringByEdit = new string[Edits.Count];
        for (var i = 0; i < Edits.Count; i++)
        {
            positionsByEdit[i] = new List<int>();
        }
        for (var i = 0; i < trackedPositions.Count; i++)
        {
            var position = trackedPositions[i];
            if (position < 0)
            {
                continue;
            }
            var editIndex = trackedEditIndices[i];
            positionsByEdit[editIndex].Add(position);
            newStringByEdit[editIndex] = trackedNewStrings[i];
        }

        var affectedRanges = new List<MultiEditFileAffectedRange>();
        var editSummaries = new List<MultiEditFileEditSummary>(Edits.Count);

        for (var editIndex = 0; editIndex < Edits.Count; editIndex++)
        {
            var positions = positionsByEdit[editIndex];
            var perEditRanges = new List<FileEditAffectedRange>(positions.Count);
            if (positions.Count > 0)
            {
                var newString = newStringByEdit[editIndex];
                foreach (var position in positions)
                {
                    perEditRanges.Add(FileEditMatching.RangeForReplacement(buffer, position, newString));
                }
            }

            var mergedPerEditRanges = FileEditMatching.MergeSameLineRanges(perEditRanges);
            var capped = FileEditMatching.CapVerboseRanges(mergedPerEditRanges);
            foreach (var range in capped.Ranges)
            {
                affectedRanges.Add(new MultiEditFileAffectedRange(editIndex, range.FromLine, range.ToLine, range.MatchCount));
            }

            editSummaries.Add(new MultiEditFileEditSummary(perEditMatchCount[editIndex], capped.Truncated));
        }

        affectedRanges.Sort((a, b) => a.FromLine.CompareTo(b.FromLine));

        ResultValue = new MultiEditFileResult(Edits.Count, editSummaries, affectedRanges);

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
