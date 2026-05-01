using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class WriteBinaryFileCommand : CommandBase, IWriteBinaryFileCommand
{
    private readonly ILogger<WriteBinaryFileCommand> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string Base64Content { get; set; } = string.Empty;

    public WriteBinaryFileCommand(
        ILogger<WriteBinaryFileCommand> logger,
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

        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;
        var resourceRegistry = resourceService.Registry;

        var resolveResult = resourceRegistry.ResolveResourcePath(FileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{FileResource}'")
                .WithErrors(resolveResult);
        }
        var isNewFile = !File.Exists(resolveResult.Value);

        var writeResult = await resourceService.FileWriter.WriteAllBytesAsync(FileResource, bytes);
        if (writeResult.IsFailure)
        {
            return writeResult;
        }

        if (isNewFile)
        {
            resourceRegistry.UpdateResourceRegistry();
        }

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void WriteBinaryFile(ResourceKey fileResource, string base64Content)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IWriteBinaryFileCommand>(command =>
        {
            command.FileResource = fileResource;
            command.Base64Content = base64Content;
        });
    }
}
