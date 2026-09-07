using Celbridge.Commands;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class GetDocumentStateCommand : CommandBase, IGetDocumentStateCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public override CommandFlags CommandFlags => CommandFlags.SuppressCommandLog;

    public DocumentStateSnapshot ResultValue { get; private set; }
        = new DocumentStateSnapshot(
            new[] { DocumentSection.MainLeft },
            Array.Empty<OpenDocumentInfo>(),
            new Dictionary<DocumentSection, ResourceKey>(),
            ResourceKey.Empty);

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

        var selectedDocuments = new Dictionary<DocumentSection, ResourceKey>();
        foreach (var section in DocumentLayoutHelper.AllSections)
        {
            var selectedDocument = documentsService.GetSelectedDocument(section);
            if (!selectedDocument.IsEmpty)
            {
                selectedDocuments[section] = selectedDocument;
            }
        }

        ResultValue = new DocumentStateSnapshot(visibleSections, openDocuments, selectedDocuments, activeDocument);

        return Result.Ok();
    }
}
