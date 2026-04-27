using System.Text.RegularExpressions;
using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class FindReplaceDocumentCommand : CommandBase, IFindReplaceDocumentCommand
{
    private readonly ILogger<FindReplaceDocumentCommand> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string SearchText { get; set; } = string.Empty;
    public string ReplaceText { get; set; } = string.Empty;
    public bool MatchCase { get; set; }
    public bool UseRegex { get; set; }
    public int FromLine { get; set; }
    public int ToLine { get; set; }
    public int ResultValue { get; private set; }

    public FindReplaceDocumentCommand(
        ILogger<FindReplaceDocumentCommand> logger,
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

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = resourceRegistry.ResolveResourcePath(FileResource);
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

        return await FindReplaceOnDisk(resourcePath);
    }

    private async Task<Result> FindReplaceOnDisk(string resourcePath)
    {
        var content = await File.ReadAllTextAsync(resourcePath);

        if (FromLine > 0 || ToLine > 0)
        {
            return await FindReplaceOnDiskScoped(resourcePath, content);
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
            var searchText = NormaliseLineEndings(SearchText, content);
            var replaceText = NormaliseLineEndings(ReplaceText, content);

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
            await File.WriteAllTextAsync(resourcePath, newContent);
        }

        return Result.Ok();
    }

    private async Task<Result> FindReplaceOnDiskScoped(string resourcePath, string content)
    {
        // Line-based replacement for scoped operations.
        // Preserves the file's original line ending style.
        var usesWindowsLineEndings = content.Contains("\r\n");
        var lineSeparator = usesWindowsLineEndings ? "\r\n" : "\n";
        var lines = content.Split('\n');

        var searchText = NormaliseLineEndings(SearchText, content);
        var replaceText = NormaliseLineEndings(ReplaceText, content);
        var comparison = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var regexOptions = MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
        var regex = UseRegex ? new Regex(searchText, regexOptions) : null;

        var replacementCount = 0;

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var lineNumber = lineIndex + 1;
            if (FromLine > 0 && lineNumber < FromLine) continue;
            if (ToLine > 0 && lineNumber > ToLine) break;

            var line = lines[lineIndex];
            if (line.EndsWith('\r'))
            {
                line = line[..^1];
            }

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

            lines[lineIndex] = usesWindowsLineEndings ? newLine + "\r" : newLine;
        }

        ResultValue = replacementCount;

        if (ResultValue > 0)
        {
            await File.WriteAllTextAsync(resourcePath, string.Join("\n", lines));
        }

        return Result.Ok();
    }

    private static string NormaliseLineEndings(string text, string fileContent)
    {
        if (!fileContent.Contains("\r\n"))
        {
            return text;
        }

        // File uses \r\n — adapt text from \n to \r\n, avoiding double-replacement of any existing \r\n
        return text.Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    //
    // Static methods for scripting support.
    //

    public static void FindReplace(ResourceKey fileResource, string searchText, string replaceText)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IFindReplaceDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
            command.SearchText = searchText;
            command.ReplaceText = replaceText;
        });
    }

    public static void FindReplace(ResourceKey fileResource, string searchText, string replaceText, bool matchCase, bool useRegex)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IFindReplaceDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
            command.SearchText = searchText;
            command.ReplaceText = replaceText;
            command.MatchCase = matchCase;
            command.UseRegex = useRegex;
        });
    }
}
