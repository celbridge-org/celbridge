using Celbridge.Documents.ViewModels;

namespace Celbridge.Code.ViewModels;

public partial class CodeEditorDocumentViewModel : DocumentViewModel
{
    public void SetFileResource(ResourceKey fileResource)
    {
        FileResource = fileResource;
    }
}
