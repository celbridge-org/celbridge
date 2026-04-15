using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class GetDocumentContextCommand : CommandBase, IGetDocumentContextCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public override CommandFlags CommandFlags => CommandFlags.Query;

    public DocumentContextSnapshot ResultValue { get; private set; }
        = new DocumentContextSnapshot(ResourceKey.Empty, 1, Array.Empty<OpenDocumentInfo>());

    public GetDocumentContextCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override Task<Result> ExecuteAsync()
    {
        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;

        var activeDocument = documentsService.ActiveDocument;
        var sectionCount = documentsService.SectionCount;
        var openDocuments = documentsService.GetOpenDocuments();

        ResultValue = new DocumentContextSnapshot(activeDocument, sectionCount, openDocuments);

        return Task.FromResult(Result.Ok());
    }
}
