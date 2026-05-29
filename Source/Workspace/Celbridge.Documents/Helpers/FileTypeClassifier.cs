using Celbridge.Workspace;

namespace Celbridge.Documents.Helpers;

/// <summary>
/// Classifies a file resource by document view type and determines whether the
/// editor stack can open it.
/// </summary>
public class FileTypeClassifier
{
    private readonly FileTypeHelper _fileTypeHelper;
    private readonly ITextBinarySniffer _textBinarySniffer;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IDocumentEditorRegistry _documentEditorRegistry;

    public FileTypeClassifier(
        FileTypeHelper fileTypeHelper,
        ITextBinarySniffer textBinarySniffer,
        IWorkspaceWrapper workspaceWrapper,
        IDocumentEditorRegistry documentEditorRegistry)
    {
        _fileTypeHelper = fileTypeHelper;
        _textBinarySniffer = textBinarySniffer;
        _workspaceWrapper = workspaceWrapper;
        _documentEditorRegistry = documentEditorRegistry;
    }

    /// <summary>
    /// Returns the document view type for the file resource. Recognised
    /// extensions resolve through FileTypeHelper; unrecognised extensions
    /// fall back to a content sniff so plain-text files with novel
    /// extensions still classify as TextDocument.
    /// </summary>
    public DocumentViewType GetDocumentViewType(ResourceKey fileResource)
    {
        var fileName = fileResource.ToString();

        if (!_fileTypeHelper.IsRecognizedExtension(fileName))
        {
            var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
            var resolveResult = resourceRegistry.ResolveResourcePath(fileResource);
            if (resolveResult.IsFailure)
            {
                return DocumentViewType.UnsupportedFormat;
            }

            var sniffResult = _textBinarySniffer.IsTextFile(resolveResult.Value);
            if (sniffResult.IsFailure)
            {
                return DocumentViewType.UnsupportedFormat;
            }

            if (!sniffResult.Value)
            {
                return DocumentViewType.UnsupportedFormat;
            }
        }

        return _fileTypeHelper.GetDocumentViewType(fileName);
    }

    /// <summary>
    /// True when the file resource can be opened in the editor stack: a
    /// registered factory advertises its extension, or the resource resolves
    /// to a non-Unsupported view type.
    /// </summary>
    public bool IsDocumentSupported(ResourceKey fileResource)
    {
        var extension = Path.GetExtension(fileResource.ToString()).ToLowerInvariant();
        if (_documentEditorRegistry.IsExtensionSupported(extension))
        {
            return true;
        }

        return GetDocumentViewType(fileResource) != DocumentViewType.UnsupportedFormat;
    }
}
