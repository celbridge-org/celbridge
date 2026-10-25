using System.Text;
using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class ReplaceFileCommand : CommandBase, IReplaceFileCommand
{
    private readonly ILogger<ReplaceFileCommand> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }

    [RedactedInLogs]
    public string Pattern { get; set; } = string.Empty;

    [RedactedInLogs]
    public string Replacement { get; set; } = string.Empty;

    public bool Literal { get; set; } = true;
    public bool MatchCase { get; set; } = true;
    public bool WholeWord { get; set; }
    public bool Multiline { get; set; }
    public int FromLine { get; set; }
    public int ToLine { get; set; }
    public ReplaceFileResult ResultValue { get; private set; } = new(0, Array.Empty<FileEditAffectedRange>(), false);

    public ReplaceFileCommand(
        ILogger<ReplaceFileCommand> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var patternOptions = new SearchPatternOptions(Literal, MatchCase, WholeWord, Multiline);
        var patternResult = SearchPattern.Compile(Pattern, patternOptions);
        if (patternResult.IsFailure)
        {
            return Result.Fail(patternResult.FirstErrorMessage);
        }
        var searchPattern = patternResult.Value;

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceFileSystem = workspaceService.ResourceService.FileSystem;

        var infoResult = await resourceFileSystem.GetInfoAsync(FileResource);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != StorageItemKind.File)
        {
            return Result.Fail($"File not found: '{FileResource}'")
                .WithErrors(infoResult);
        }

        return await ReplaceOnDisk(resourceFileSystem, searchPattern);
    }

    private async Task<Result> ReplaceOnDisk(IResourceFileSystem resourceFileSystem, SearchPattern searchPattern)
    {
        var readResult = await resourceFileSystem.ReadAllTextAsync(FileResource);
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read file: '{FileResource}'")
                .WithErrors(readResult);
        }
        var content = readResult.Value;

        // A line break in the replacement takes the file's own line ending.
        var separator = LineEndingHelper.DetectSeparatorOrDefault(content);
        var replacementText = LineEndingHelper.ConvertLineEndings(Replacement, separator);
        var replacementResult = searchPattern.CompileReplacement(replacementText);
        if (replacementResult.IsFailure)
        {
            return Result.Fail(replacementResult.FirstErrorMessage);
        }
        var replacement = replacementResult.Value;

        var (scopeStart, scopeEnd) = FindScope(content);
        var scopeText = content.Substring(scopeStart, scopeEnd - scopeStart);
        var matches = searchPattern.FindMatches(scopeText);

        // Match positions in the post-edit buffer plus the actual substituted
        // text for each match. Group references can make every match's
        // substitution different, so we capture per-match output to compute
        // accurate line ranges later.
        var matchOffsets = new List<int>(matches.Count);
        var matchSubstitutions = new List<string>(matches.Count);
        var newContent = new StringBuilder(content.Length);
        var position = 0;
        foreach (var match in matches)
        {
            var matchStart = scopeStart + match.Start;
            newContent.Append(content, position, matchStart - position);
            matchOffsets.Add(newContent.Length);

            var substitution = replacement.Expand(match);
            matchSubstitutions.Add(substitution);
            newContent.Append(substitution);

            position = matchStart + match.Length;
        }
        newContent.Append(content, position, content.Length - position);

        var replacementCount = matchOffsets.Count;
        var newText = newContent.ToString();

        if (replacementCount > 0)
        {
            var writeResult = await resourceFileSystem.WriteAllTextAsync(FileResource, newText);
            if (writeResult.IsFailure)
            {
                return writeResult;
            }
        }

        var affectedRanges = new List<FileEditAffectedRange>(replacementCount);
        for (var i = 0; i < replacementCount; i++)
        {
            affectedRanges.Add(FileEditMatching.RangeForReplacement(newText, matchOffsets[i], matchSubstitutions[i]));
        }

        var mergedRanges = FileEditMatching.MergeSameLineRanges(affectedRanges);
        var capped = FileEditMatching.CapVerboseRanges(mergedRanges);
        ResultValue = new ReplaceFileResult(replacementCount, capped.Ranges, capped.Truncated);

        return Result.Ok();
    }

    // Returns the span of the content that FromLine and ToLine select. The span runs from the start of the first line
    // to the end of the last line's text, before its line break. A line ends at '\n', with or without a '\r' before it.
    private (int Start, int End) FindScope(string content)
    {
        if (FromLine <= 0
            && ToLine <= 0)
        {
            return (0, content.Length);
        }

        var lineStarts = new List<int>
        {
            0,
        };
        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] == '\n'
                && index + 1 < content.Length)
            {
                lineStarts.Add(index + 1);
            }
        }

        var start = 0;
        if (FromLine > 0)
        {
            if (FromLine > lineStarts.Count)
            {
                return (content.Length, content.Length);
            }
            start = lineStarts[FromLine - 1];
        }

        var end = content.Length;
        if (ToLine > 0
            && ToLine < lineStarts.Count)
        {
            end = lineStarts[ToLine] - 1;
            if (end > 0
                && content[end - 1] == '\r')
            {
                end--;
            }
        }
        else if (ToLine > 0)
        {
            end = content.Length;
            if (end > 0
                && content[end - 1] == '\n')
            {
                end--;
                if (end > 0
                    && content[end - 1] == '\r')
                {
                    end--;
                }
            }
        }

        if (end < start)
        {
            return (start, start);
        }

        return (start, end);
    }
}
