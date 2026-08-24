using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class CloseDocumentCommand : CommandBase, ICloseDocumentCommand
{
    public override CommandFlags CommandFlags => CommandFlags.SaveWorkspaceState;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }

    public bool ForceClose { get; set; }

    public bool SelectNeighbour { get; set; } = true;

    public CloseDocumentCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var workspaceService = _workspaceWrapper.WorkspaceService;
        var documentsService = workspaceService.DocumentsService;

        // A docked utility is never destroyed by a close: closing its document tab docks it back into the Utility
        // Panel instead. Handling it here, at the one command every close routes through, keeps that rule
        // independent of which affordance triggered the close.
        var utilityService = workspaceService.UtilityService;
        if (utilityService.GetDockedUtilityId(FileResource) is { } dockedUtilityId)
        {
            return await utilityService.DockUtilityAsync(dockedUtilityId, DockLocation.UtilityPanel);
        }

        var closeOptions = new CloseDocumentOptions(ForceClose, SelectNeighbour);

        var closeResult = await documentsService.CloseDocument(FileResource, closeOptions);
        if (closeResult.IsFailure)
        {
            return Result.Fail($"Failed to close document for file resource '{FileResource}'")
                .WithErrors(closeResult);
        }

        return Result.Ok();
    }
}
