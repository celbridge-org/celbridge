using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class WriteBinaryFileCommand : CommandBase, IWriteBinaryFileCommand
{
    // See WriteFileCommand for why this command always refreshes the registry.
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    private readonly ILogger<WriteBinaryFileCommand> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }

    [RedactedInLogs]
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

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceFileSystem = workspaceService.ResourceService.FileSystem;

        var writeResult = await resourceFileSystem.WriteAllBytesAsync(FileResource, bytes);
        if (writeResult.IsFailure)
        {
            return writeResult;
        }

        return Result.Ok();
    }
}
