using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class GetDocumentStateCommand : CommandBase, IGetDocumentStateCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public override CommandFlags CommandFlags => CommandFlags.SuppressCommandLog;

    public DocumentStateSnapshot ResultValue { get; private set; }
        = new DocumentStateSnapshot(
            ResourceKey.Empty,
            new[] { DocumentSection.MainLeft },
            Array.Empty<OpenDocumentInfo>());

    public GetDocumentStateCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;

        var activeDocument = documentsService.ActiveDocument;
        var visibleSections = documentsService.VisibleSections;
        var openDocuments = documentsService.GetOpenDocuments();

        ResultValue = new DocumentStateSnapshot(activeDocument, visibleSections, openDocuments);

        return Result.Ok();
    }
}
