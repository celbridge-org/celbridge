using System.Text.RegularExpressions;
using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class FindReplaceFileCommand : CommandBase, IFindReplaceFileCommand
{
    private readonly ILogger<FindReplaceFileCommand> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string SearchText { get; set; } = string.Empty;
    public string ReplaceText { get; set; } = string.Empty;
    public bool MatchCase { get; set; }
    public bool UseRegex { get; set; }
    public int FromLine { get; set; }
    public int ToLine { get; set; }
    public int ResultValue { get; private set; }

    public FindReplaceFileCommand(
        ILogger<FindReplaceFileCommand> logger,
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

        return await FindReplaceOnDisk(resourceService, resourcePath);
    }

    private async Task<Result> FindReplaceOnDisk(IResourceService resourceService, string resourcePath)
    {
        var content = await File.ReadAllTextAsync(resourcePath);

        if (FromLine > 0 || ToLine > 0)
        {
            return await FindReplaceOnDiskScoped(resourceService, content);
        }

        string newContent;
        if (UseRegex)
        {
            var regexOptions = MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
            var regex = new Regex(SearchText, regexOptions);
            var matchCount = regex.Matches(content).Count;
            newContent = regex.Replace(content, ReplaceText);
            ResultValue = matchCount;
        }
        else
        {
            // Normalise the search and replacement text to match the file's actual line
            // endings. Agents always construct strings with \n. Files on Windows use \r\n.
            var fileSeparator = LineEndingHelper.DetectSeparatorOrDefault(content);
            var searchText = LineEndingHelper.ConvertLineEndings(SearchText, fileSeparator);
            var replaceText = LineEndingHelper.ConvertLineEndings(ReplaceText, fileSeparator);

            var comparison = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var replacementCount = 0;
            var searchIndex = 0;
            var result = new System.Text.StringBuilder();

            while (searchIndex < content.Length)
            {
                var matchIndex = content.IndexOf(searchText, searchIndex, comparison);
                if (matchIndex < 0)
                {
                    result.Append(content, searchIndex, content.Length - searchIndex);
                    break;
                }

                result.Append(content, searchIndex, matchIndex - searchIndex);
                result.Append(replaceText);
                searchIndex = matchIndex + searchText.Length;
                replacementCount++;
            }

            newContent = result.ToString();
            ResultValue = replacementCount;
        }

        if (ResultValue > 0)
        {
            return await resourceService.FileWriter.WriteAllTextAsync(FileResource, newContent);
        }

        return Result.Ok();
    }

    private async Task<Result> FindReplaceOnDiskScoped(IResourceService resourceService, string content)
    {
        // Line-based replacement for scoped operations.
        // Preserves the file's original line ending style and trailing-newline state.
        var separator = LineEndingHelper.DetectSeparatorOrDefault(content);
        var endsWithNewline = LineEndingHelper.EndsWithNewline(content);
        var lines = LineEndingHelper.SplitToContentLines(content);

        var searchText = LineEndingHelper.ConvertLineEndings(SearchText, separator);
        var replaceText = LineEndingHelper.ConvertLineEndings(ReplaceText, separator);
        var comparison = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var regexOptions = MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
        var regex = UseRegex ? new Regex(searchText, regexOptions) : null;

        var replacementCount = 0;

        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var lineNumber = lineIndex + 1;
            if (FromLine > 0 && lineNumber < FromLine) continue;
            if (ToLine > 0 && lineNumber > ToLine) break;

            var line = lines[lineIndex];

            string newLine;
            if (regex is not null)
            {
                var matchCount = regex.Matches(line).Count;
                newLine = regex.Replace(line, replaceText);
                replacementCount += matchCount;
            }
            else
            {
                var sb = new System.Text.StringBuilder();
                var searchOffset = 0;
                var lineReplacements = 0;

                while (searchOffset < line.Length)
                {
                    var matchPosition = line.IndexOf(searchText, searchOffset, comparison);
                    if (matchPosition < 0)
                    {
                        sb.Append(line, searchOffset, line.Length - searchOffset);
                        break;
                    }
                    sb.Append(line, searchOffset, matchPosition - searchOffset);
                    sb.Append(replaceText);
                    searchOffset = matchPosition + searchText.Length;
                    lineReplacements++;
                }

                newLine = sb.ToString();
                replacementCount += lineReplacements;
            }

            lines[lineIndex] = newLine;
        }

        ResultValue = replacementCount;

        if (ResultValue > 0)
        {
            var output = string.Join(separator, lines);
            if (endsWithNewline && output.Length > 0)
            {
                output += separator;
            }

            return await resourceService.FileWriter.WriteAllTextAsync(FileResource, output);
        }

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void FindReplace(ResourceKey fileResource, string searchText, string replaceText)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IFindReplaceFileCommand>(command =>
        {
            command.FileResource = fileResource;
            command.SearchText = searchText;
            command.ReplaceText = replaceText;
        });
    }

    public static void FindReplace(ResourceKey fileResource, string searchText, string replaceText, bool matchCase, bool useRegex)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IFindReplaceFileCommand>(command =>
        {
            command.FileResource = fileResource;
            command.SearchText = searchText;
            command.ReplaceText = replaceText;
            command.MatchCase = matchCase;
            command.UseRegex = useRegex;
        });
    }
}
