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

        // Resolve the file path, creating the file on disk if it doesn't exist yet.
        // This allows document_write to be used as a single-step file creation + write.
        var resolveResult = resourceRegistry.ResolveResourcePath(FileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{FileResource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        var isNewFile = !File.Exists(resourcePath);
        if (isNewFile)
        {
            var parentFolder = Path.GetDirectoryName(resourcePath);
            if (!string.IsNullOrEmpty(parentFolder))
            {
                Directory.CreateDirectory(parentFolder);
            }
            await File.WriteAllTextAsync(resourcePath, Content);

            // Update the resource registry so the new file is immediately visible
            resourceRegistry.UpdateResourceRegistry();

            if (!OpenDocument)
            {
                return Result.Ok();
            }
        }

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

            // For new files we already wrote the content to disk, so just open without applying edits
            if (isNewFile)
            {
                return Result.Ok();
            }

            // Ensure any unsaved editor changes are flushed to disk before reading
            if (documentView.HasUnsavedChanges)
            {
                var saveResult = await documentView.SaveDocument();
                if (saveResult.IsFailure)
                {
                    return Result.Fail($"Failed to save document before writing: '{FileResource}'")
                        .WithErrors(saveResult);
                }
            }

            // Read the current content to determine the document's line count
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
            // Write directly to disk (file already exists since isNewFile was handled above)
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
