using Celbridge.Commands;
using Celbridge.Downloads;
using Celbridge.Projects;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Commands;

public class MoveDownloadCommand : CommandBase, IMoveDownloadCommand
{
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    public ResourceKey SourceResource { get; set; }
    public ResourceKey DestResource { get; set; }

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public MoveDownloadCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return Result.Fail("Cannot move a download because no workspace is loaded");
        }

        // Only a staged download moves this way, so the command cannot take a file out of the project.
        if (SourceResource.Root != ProjectConstants.TempFolder)
        {
            return Result.Fail($"A download can only be moved from temp:, not from '{SourceResource}'");
        }

        // Through the gateway rather than the operation service, so the destination is policy-checked like
        // any other write and nothing is recorded for undo. Nothing in the project references the staged
        // file, so the move out of temp: has no identity to carry with it.
        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        var moveOptions = new MoveOptions(AllowCrossRoot: true);

        var moveResult = await resourceFileSystem.MoveAsync(SourceResource, DestResource, moveOptions);
        if (moveResult.IsFailure)
        {
            return Result.Fail($"Failed to move the download from '{SourceResource}' to '{DestResource}'")
                .WithErrors(moveResult);
        }

        return Result.Ok();
    }
}
