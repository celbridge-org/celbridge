using Celbridge.Commands;
using Celbridge.Community;
using Celbridge.Documents;

namespace Celbridge.WorkspaceUI.Commands;

public class OpenCommunityLinkCommand : CommandBase, IOpenCommunityLinkCommand
{
    public override CommandFlags CommandFlags => CommandFlags.None;

    private readonly ICommandService _commandService;
    private readonly ICommunityService _communityService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public string LinkId { get; set; } = string.Empty;

    public OpenCommunityLinkCommand(
        ICommandService commandService,
        ICommunityService communityService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _commandService = commandService;
        _communityService = communityService;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return Result.Fail($"Failed to open community link '{LinkId}' because no workspace is loaded");
        }

        var link = _communityService.FindLink(LinkId);
        if (link is null)
        {
            return Result.Fail($"Failed to open community link because no link has the id '{LinkId}'");
        }

        var resource = _communityService.GetLinkResource(link);

        // Rewriting an open document would bounce through the file watcher and reload the web view, losing
        // the page the user is reading. Regenerating is for getting back to a working state, so it applies
        // only when the document is not already on screen.
        if (!IsDocumentOpen(resource))
        {
            var writeResult = await _communityService.WriteLinkDocumentAsync(link);
            if (writeResult.IsFailure)
            {
                return Result.Fail($"Failed to open community link '{LinkId}'")
                    .WithErrors(writeResult);
            }
        }

        // Queued rather than awaited, because awaiting a command from inside a running command deadlocks the
        // queue. An already-open document is activated in its current section rather than pulled into Main.
        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = resource;
        });

        return Result.Ok();
    }

    private bool IsDocumentOpen(ResourceKey resource)
    {
        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;

        var openDocuments = documentsService.GetOpenDocuments();
        foreach (var openDocument in openDocuments)
        {
            if (openDocument.FileResource == resource)
            {
                return true;
            }
        }

        return false;
    }
}
