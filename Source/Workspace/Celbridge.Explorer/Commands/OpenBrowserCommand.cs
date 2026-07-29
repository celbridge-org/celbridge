using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Explorer.Commands;

public class OpenBrowserCommand : CommandBase, IOpenBrowserCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public string URL { get; set; } = string.Empty;

    public OpenBrowserCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var explorerService = _workspaceWrapper.WorkspaceService.ExplorerService;

        var openResult = await explorerService.OpenBrowser(URL);
        if (openResult.IsFailure)
        {
            return openResult;
        }

        return Result.Ok();
    }
}
