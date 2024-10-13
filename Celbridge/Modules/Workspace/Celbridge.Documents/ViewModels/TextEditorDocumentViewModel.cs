using Celbridge.ExtensionAPI;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

using Path = System.IO.Path;

namespace Celbridge.Documents.ViewModels;

public class TextEditorDocumentViewModel : ObservableObject
{
    private readonly IDocumentsService _documentsService;

    public TextEditorDocumentViewModel(IWorkspaceWrapper workspaceWrapper)
    {
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;
    }

    public Result<PreviewProvider> GetPreviewProvider(ResourceKey fileResource)
    {
        var fileExtension = Path.GetExtension(fileResource);
        if (string.IsNullOrEmpty(fileExtension))
        {
            return Result<PreviewProvider>.Fail();
        }

        var getResult = _documentsService.GetPreviewProvider(fileExtension);
        if (getResult.IsFailure)
        {
            return getResult;
        }

        var provider = getResult.Value;

        return Result<PreviewProvider>.Ok(provider);
    }
}
