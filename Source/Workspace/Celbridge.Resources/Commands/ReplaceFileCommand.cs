using System.Text;
using System.Text.RegularExpressions;
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
    public string SearchText { get; set; } = string.Empty;
    public string ReplaceText { get; set; } = string.Empty;
    public bool MatchCase { get; set; }
    public bool MatchWord { get; set; }
    public bool UseRegex { get; set; }
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
        if (string.IsNullOrEmpty(SearchText))
        {
            return Result.Fail("Search text cannot be empty");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var fileSystem = workspaceService.ResourceFileSystem;

        var infoResult = await fileSystem.GetInfoAsync(FileResource);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != ResourceInfoKind.File)
        {
            return Result.Fail($"File not found: '{FileResource}'");
        }

        return await ReplaceOnDisk(fileSystem);
    }

    private async Task<Result> ReplaceOnDisk(IResourceFileSystem fileSystem)
    {
        var readResult = await fileSystem.ReadAllTextAsync(FileResource);
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read file: '{FileResource}'")
                .WithErrors(readResult);
        }
        var content = readResult.Value;

        // Match positions in the post-edit buffer plus the actual substituted
        // text for each match. Regex back-references can make every match's
        // substitution different, so we capture per-match output to compute
        // accurate line ranges later.
        var matchOffsets = new List<int>();
        var matchSubstitutions = new List<string>();
        string newContent;

        if (FromLine > 0 || ToLine > 0)
        {
            newContent = ApplyScopedReplacement(content, matchOffsets, matchSubstitutions);
        }
        else
        {
            newContent = ApplyUnscopedReplacement(content, matchOffsets, matchSubstitutions);
        }

        var replacementCount = matchOffsets.Count;

        if (replacementCount > 0)
        {
            var writeResult = await fileSystem.WriteAllTextAsync(FileResource, newContent);
            if (writeResult.IsFailure)
            {
                return writeResult;
            }
        }

        var affectedRanges = new List<FileEditAffectedRange>(replacementCount);
        for (var i = 0; i < replacementCount; i++)
        {
            affectedRanges.Add(FileEditMatching.RangeForReplacement(newContent, matchOffsets[i], matchSubstitutions[i]));
        }

        var mergedRanges = FileEditMatching.MergeSameLineRanges(affectedRanges);
        var capped = FileEditMatching.CapVerboseRanges(mergedRanges);
        ResultValue = new ReplaceFileResult(replacementCount, capped.Ranges, capped.Truncated);

        return Result.Ok();
    }

    /// <summary>
    /// When MatchWord is requested without UseRegex, the literal search is
    /// translated into a regex with word-boundary anchors so the matcher can
    /// honour the boundary constraint. UseRegex takes precedence — regex
    /// callers add their own \b anchors if they want word matching.
    /// </summary>
    private bool ShouldRouteThroughRegex => UseRegex || MatchWord;

    private string BuildEffectiveRegexPattern(string normalisedSearchText)
    {
        if (UseRegex)
        {
            return normalisedSearchText;
        }
        return $@"\b{Regex.Escape(normalisedSearchText)}\b";
    }

    private string ApplyUnscopedReplacement(string content, List<int> matchOffsets, List<string> matchSubstitutions)
    {
        var sb = new StringBuilder(content.Length);

        if (ShouldRouteThroughRegex)
        {
            // UseRegex passes SearchText verbatim; MatchWord wraps the escaped
            // literal in \b...\b. Either way, ReplaceText is used as the
            // substitution template (back-references supported by Match.Result).
            var fileSeparator = LineEndingHelper.DetectSeparatorOrDefault(content);
            var normalisedSearch = UseRegex
                ? SearchText
                : LineEndingHelper.ConvertLineEndings(SearchText, fileSeparator);
            var pattern = BuildEffectiveRegexPattern(normalisedSearch);
            var regexOptions = MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
            var regex = new Regex(pattern, regexOptions);
            var lastEnd = 0;
            foreach (Match match in regex.Matches(content))
            {
                sb.Append(content, lastEnd, match.Index - lastEnd);
                matchOffsets.Add(sb.Length);
                var substitution = match.Result(ReplaceText);
                matchSubstitutions.Add(substitution);
                sb.Append(substitution);
                lastEnd = match.Index + match.Length;
            }
            sb.Append(content, lastEnd, content.Length - lastEnd);
            return sb.ToString();
        }

        // Normalise the search and replacement text to match the file's actual
        // line endings. Agents typically construct strings with \n. Files on
        // Windows use \r\n.
        var separator = LineEndingHelper.DetectSeparatorOrDefault(content);
        var searchText = LineEndingHelper.ConvertLineEndings(SearchText, separator);
        var replaceText = LineEndingHelper.ConvertLineEndings(ReplaceText, separator);
        var comparison = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        var searchIndex = 0;
        while (searchIndex < content.Length)
        {
            var matchIndex = content.IndexOf(searchText, searchIndex, comparison);
            if (matchIndex < 0)
            {
                sb.Append(content, searchIndex, content.Length - searchIndex);
                break;
            }

            sb.Append(content, searchIndex, matchIndex - searchIndex);
            matchOffsets.Add(sb.Length);
            matchSubstitutions.Add(replaceText);
            sb.Append(replaceText);
            searchIndex = matchIndex + searchText.Length;
        }

        return sb.ToString();
    }

    private string ApplyScopedReplacement(string content, List<int> matchOffsets, List<string> matchSubstitutions)
    {
        // Line-based replacement for scoped operations. Multi-line patterns
        // do not match across line breaks here — the line-by-line strategy
        // preserves the historical behaviour and keeps positions easy to
        // track. Per-edit absolute offsets in the post-edit buffer are
        // accumulated as each new line is built.
        var separator = LineEndingHelper.DetectSeparatorOrDefault(content);
        var endsWithNewline = LineEndingHelper.EndsWithNewline(content);
        var lines = LineEndingHelper.SplitToContentLines(content);

        // The scoped path matches line-by-line against content with line
        // terminators stripped, so we normalise every variant of SearchText
        // here (including the UseRegex case). A literal \n in a regex pattern
        // could never fire against an isolated line anyway. The unscoped
        // branch is different: it passes the regex pattern through verbatim
        // so the caller keeps full control over \n vs \r\n semantics.
        var searchText = LineEndingHelper.ConvertLineEndings(SearchText, separator);
        var replaceText = LineEndingHelper.ConvertLineEndings(ReplaceText, separator);
        var comparison = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var regexOptions = MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;

        Regex? regex = null;
        if (ShouldRouteThroughRegex)
        {
            regex = new Regex(BuildEffectiveRegexPattern(searchText), regexOptions);
        }

        var newLines = new List<string>(lines.Count);
        var absoluteOffset = 0;

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var lineNumber = lineIndex + 1;

            string newLine;
            if ((FromLine > 0 && lineNumber < FromLine) ||
                (ToLine > 0 && lineNumber > ToLine))
            {
                newLine = line;
            }
            else if (regex is not null)
            {
                var sb = new StringBuilder();
                var lastEnd = 0;
                foreach (Match match in regex.Matches(line))
                {
                    sb.Append(line, lastEnd, match.Index - lastEnd);
                    matchOffsets.Add(absoluteOffset + sb.Length);
                    var substitution = match.Result(replaceText);
                    matchSubstitutions.Add(substitution);
                    sb.Append(substitution);
                    lastEnd = match.Index + match.Length;
                }
                sb.Append(line, lastEnd, line.Length - lastEnd);
                newLine = sb.ToString();
            }
            else
            {
                var sb = new StringBuilder();
                var searchOffset = 0;
                while (searchOffset < line.Length)
                {
                    var matchPosition = line.IndexOf(searchText, searchOffset, comparison);
                    if (matchPosition < 0)
                    {
                        sb.Append(line, searchOffset, line.Length - searchOffset);
                        break;
                    }
                    sb.Append(line, searchOffset, matchPosition - searchOffset);
                    matchOffsets.Add(absoluteOffset + sb.Length);
                    matchSubstitutions.Add(replaceText);
                    sb.Append(replaceText);
                    searchOffset = matchPosition + searchText.Length;
                }
                newLine = sb.ToString();
            }

            newLines.Add(newLine);
            absoluteOffset += newLine.Length + separator.Length;
        }

        var output = string.Join(separator, newLines);
        if (endsWithNewline && output.Length > 0)
        {
            output += separator;
        }

        return output;
    }

    //
    // Static methods for scripting support.
    //

    public static void Replace(ResourceKey fileResource, string searchText, string replaceText)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IReplaceFileCommand>(command =>
        {
            command.FileResource = fileResource;
            command.SearchText = searchText;
            command.ReplaceText = replaceText;
        });
    }

    public static void Replace(ResourceKey fileResource, string searchText, string replaceText, bool matchCase, bool useRegex)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IReplaceFileCommand>(command =>
        {
            command.FileResource = fileResource;
            command.SearchText = searchText;
            command.ReplaceText = replaceText;
            command.MatchCase = matchCase;
            command.UseRegex = useRegex;
        });
    }
}
