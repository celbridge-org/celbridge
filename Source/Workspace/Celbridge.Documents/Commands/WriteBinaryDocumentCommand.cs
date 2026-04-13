using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class WriteBinaryDocumentCommand : CommandBase, IWriteBinaryDocumentCommand
{
    private readonly ILogger<WriteBinaryDocumentCommand> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string Base64Content { get; set; } = string.Empty;
    public bool OpenDocument { get; set; } = true;

    public WriteBinaryDocumentCommand(
        ILogger<WriteBinaryDocumentCommand> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(Base64Content);
        }
        catch (FormatException)
        {
            return Result.Fail("Invalid base64 content");
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

        // Check if document is already open
        var documentView = documentsPanel.GetDocumentView(FileResource);

        if (documentView is not null || OpenDocument)
        {
            // Write the bytes to disk first, then open/reload the document so the
            // specialized editor picks up the new content
            await File.WriteAllBytesAsync(resourcePath, bytes);

            if (documentView is not null)
            {
                // Document is already open, force reload to pick up new content
                var openResult = await documentsService.OpenDocument(FileResource, new OpenDocumentOptions(ForceReload: true, Activate: false));
                if (openResult.IsFailure)
                {
                    return Result.Fail($"Failed to reload binary document: '{FileResource}'")
                        .WithErrors(openResult);
                }
            }
            else
            {
                // Open the document so the editor can manage it
                var openResult = await documentsService.OpenDocument(FileResource, new OpenDocumentOptions(Activate: false));
                if (openResult.IsFailure)
                {
                    return Result.Fail($"Failed to open binary document: '{FileResource}'")
                        .WithErrors(openResult);
                }
            }
        }
        else
        {
            // Write directly to disk without opening.
            // Ensure the parent folder exists so new files can be created.
            var parentFolder = Path.GetDirectoryName(resourcePath);
            if (!string.IsNullOrEmpty(parentFolder) && !Directory.Exists(parentFolder))
            {
                Directory.CreateDirectory(parentFolder);
            }

            await File.WriteAllBytesAsync(resourcePath, bytes);
        }

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void WriteBinaryDocument(ResourceKey fileResource, string base64Content)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IWriteBinaryDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
            command.Base64Content = base64Content;
        });
    }
}
