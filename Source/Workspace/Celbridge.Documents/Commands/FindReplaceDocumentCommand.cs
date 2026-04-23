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
    public bool OpenDocument { get; set; } = true;
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

        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;
        var documentsPanel = _workspaceWrapper.WorkspaceService.DocumentsPanel;
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

        // Check if document is already open
        var documentView = documentsPanel.GetDocumentView(FileResource);

        if (documentView is not null || OpenDocument)
        {
            // Route through the editor for undo support
            return await FindReplaceThroughEditor(documentsService, documentsPanel, documentView, resourcePath);
        }
        else
        {
            // Apply replacements directly to the file on disk
            return await FindReplaceOnDisk(resourcePath);
        }
    }

    private async Task<Result> FindReplaceThroughEditor(
        IDocumentsService documentsService,
        IDocumentsPanel documentsPanel,
        IDocumentView? documentView,
        string resourcePath)
    {
        if (documentView is null)
        {
            var openResult = await documentsService.OpenDocument(FileResource, new OpenDocumentOptions(Activate: false));
            if (openResult.IsFailure)
            {
                return Result.Fail($"Failed to open document: '{FileResource}'")
                    .WithErrors(openResult);
            }

            documentView = documentsPanel.GetDocumentView(FileResource);
            if (documentView is null)
            {
                return Result.Fail($"Document view not found after opening: '{FileResource}'");
            }
        }

        // Ensure any unsaved editor changes are flushed to disk before reading
        if (documentView.HasUnsavedChanges)
        {
            var saveResult = await documentView.SaveDocument();
            if (saveResult.IsFailure)
            {
                return Result.Fail($"Failed to save document before find/replace: '{FileResource}'")
                    .WithErrors(saveResult);
            }
        }

        // Read the file to find match positions
        var content = await File.ReadAllTextAsync(resourcePath);
        var lines = content.Split('\n');
        var edits = BuildTextEditsFromMatches(lines);

        if (edits.Count == 0)
        {
            ResultValue = 0;
            return Result.Ok();
        }

        var applyResult = await documentView.ApplyEditsAsync(edits);
        if (applyResult.IsFailure)
        {
            return Result.Fail($"Failed to apply find/replace edits to document: '{FileResource}'")
                .WithErrors(applyResult);
        }

        // Flush the edits to disk so MCP callers' follow-up file_read sees the post-edit
        // state. Do NOT gate on HasUnsavedChanges: ApplyEditsAsync is a fire-and-forget
        // notification, and the document/changed round-trip that flips the flag often
        // hasn't arrived by the time this line runs. Same pattern as ApplyEditsCommand's
        // ForceSave branch.
        var saveAfterEditsResult = await documentView.SaveDocument();
        if (saveAfterEditsResult.IsFailure)
        {
            return Result.Fail($"Failed to save document after find/replace: '{FileResource}'")
                .WithErrors(saveAfterEditsResult);
        }

        ResultValue = edits.Count;
        return Result.Ok();
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
            // endings. Agents always construct strings with \n; files on Windows use \r\n.
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

    private List<TextEdit> BuildTextEditsFromMatches(string[] lines)
    {
        var edits = new List<TextEdit>();
        var regexOptions = MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;

        if (UseRegex)
        {
            var regex = new Regex(SearchText, regexOptions);
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var lineNumber = lineIndex + 1;
                if (FromLine > 0 && lineNumber < FromLine) continue;
                if (ToLine > 0 && lineNumber > ToLine) break;

                var line = lines[lineIndex];
                // Strip trailing \r if present (lines split on \n)
                if (line.EndsWith('\r'))
                {
                    line = line[..^1];
                }

                var matches = regex.Matches(line);
                // Process matches in reverse order to avoid position shifts
                for (int matchIndex = matches.Count - 1; matchIndex >= 0; matchIndex--)
                {
                    var match = matches[matchIndex];
                    var replacementText = match.Result(ReplaceText);
                    var startColumn = match.Index + 1;
                    var endColumn = match.Index + match.Length + 1;
                    edits.Add(new TextEdit(lineIndex + 1, startColumn, lineIndex + 1, endColumn, replacementText));
                }
            }
        }
        else
        {
            var comparison = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
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

                var searchOffset = 0;
                var lineMatches = new List<(int Start, int Length)>();

                while (searchOffset < line.Length)
                {
                    var matchPosition = line.IndexOf(SearchText, searchOffset, comparison);
                    if (matchPosition < 0)
                    {
                        break;
                    }

                    lineMatches.Add((matchPosition, SearchText.Length));
                    searchOffset = matchPosition + SearchText.Length;
                }

                // Add edits in reverse order for this line
                for (int i = lineMatches.Count - 1; i >= 0; i--)
                {
                    var (start, length) = lineMatches[i];
                    var startColumn = start + 1;
                    var endColumn = start + length + 1;
                    edits.Add(new TextEdit(lineIndex + 1, startColumn, lineIndex + 1, endColumn, ReplaceText));
                }
            }
        }

        return edits;
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
