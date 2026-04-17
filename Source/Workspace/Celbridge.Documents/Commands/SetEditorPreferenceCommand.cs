using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class SetEditorPreferenceCommand : CommandBase, ISetEditorPreferenceCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public string Extension { get; set; } = string.Empty;

    public DocumentEditorId EditorId { get; set; }

    public SetEditorPreferenceCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;
        await documentsService.SetEditorPreferenceAsync(Extension, EditorId);
        return Result.Ok();
    }
}
