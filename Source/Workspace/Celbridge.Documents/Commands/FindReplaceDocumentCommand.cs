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
        var resourcePath = resourceRegistry.GetResourcePath(FileResource);

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
            var openResult = await documentsService.OpenDocument(FileResource, forceReload: false, location: string.Empty, activate: false);
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

        ResultValue = edits.Count;
        return Result.Ok();
    }

    private async Task<Result> FindReplaceOnDisk(string resourcePath)
    {
        var content = await File.ReadAllTextAsync(resourcePath);

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
            var comparison = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var replacementCount = 0;
            var searchIndex = 0;
            var result = new System.Text.StringBuilder();

            while (searchIndex < content.Length)
            {
                var matchIndex = content.IndexOf(SearchText, searchIndex, comparison);
                if (matchIndex < 0)
                {
                    result.Append(content, searchIndex, content.Length - searchIndex);
                    break;
                }

                result.Append(content, searchIndex, matchIndex - searchIndex);
                result.Append(ReplaceText);
                searchIndex = matchIndex + SearchText.Length;
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

    private List<TextEdit> BuildTextEditsFromMatches(string[] lines)
    {
        var edits = new List<TextEdit>();
        var regexOptions = MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;

        if (UseRegex)
        {
            var regex = new Regex(SearchText, regexOptions);
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
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
