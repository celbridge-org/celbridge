using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class WriteDocumentCommand : CommandBase, IWriteDocumentCommand
{
    private readonly ILogger<WriteDocumentCommand> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool OpenDocument { get; set; } = true;

    public WriteDocumentCommand(
        ILogger<WriteDocumentCommand> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;
        var documentsPanel = _workspaceWrapper.WorkspaceService.DocumentsPanel;
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        // Check if document is already open
        var documentView = documentsPanel.GetDocumentView(FileResource);

        if (documentView is not null || OpenDocument)
        {
            // Route through the editor for undo support
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

            // Read the current content to determine the document's line count
            var resourcePath = resourceRegistry.GetResourcePath(FileResource);
            var existingLines = await File.ReadAllLinesAsync(resourcePath);
            var lineCount = existingLines.Length;

            // Build a single edit that replaces the entire content
            var lastLineLength = lineCount > 0 ? existingLines[lineCount - 1].Length : 0;
            var endLine = Math.Max(1, lineCount);
            var endColumn = lastLineLength + 1;

            var fullReplaceEdit = new TextEdit(1, 1, endLine, endColumn, Content);
            var applyResult = await documentView.ApplyEditsAsync(new[] { fullReplaceEdit });
            if (applyResult.IsFailure)
            {
                return Result.Fail($"Failed to write content to document: '{FileResource}'")
                    .WithErrors(applyResult);
            }
        }
        else
        {
            // Write directly to disk
            var resourcePath = resourceRegistry.GetResourcePath(FileResource);
            if (!File.Exists(resourcePath))
            {
                return Result.Fail($"File not found: '{FileResource}'");
            }

            await File.WriteAllTextAsync(resourcePath, Content);
        }

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void WriteDocument(ResourceKey fileResource, string content)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IWriteDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
            command.Content = content;
        });
    }

    public static void WriteDocument(ResourceKey fileResource, string content, bool openDocument)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IWriteDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
            command.Content = content;
            command.OpenDocument = openDocument;
        });
    }
}
