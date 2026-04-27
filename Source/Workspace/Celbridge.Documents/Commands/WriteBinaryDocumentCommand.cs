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

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

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
        }

        await File.WriteAllBytesAsync(resourcePath, bytes);

        if (isNewFile)
        {
            resourceRegistry.UpdateResourceRegistry();
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
