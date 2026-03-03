using Celbridge.Documents.ViewModels;

namespace Celbridge.Code.ViewModels;

public partial class TextEditorDocumentViewModel : DocumentViewModel
{
    public void SetFileResource(ResourceKey fileResource)
    {
        FileResource = fileResource;
    }
}
