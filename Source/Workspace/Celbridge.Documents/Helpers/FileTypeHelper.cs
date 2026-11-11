using Celbridge.Explorer;
using Celbridge.Packages;

namespace Celbridge.Documents.Helpers;

public class FileTypeHelper
{
    private const string PlaintextLanguage = "plaintext";

    private IDocumentEditorRegistry? _documentEditorRegistry;

    /// <summary>
    /// Sets the document editor registry for querying language mappings and supported extensions.
    /// </summary>
    public void SetDocumentEditorRegistry(IDocumentEditorRegistry registry)
    {
        _documentEditorRegistry = registry;
    }

    /// <summary>
    /// Returns the document view type for a file based on its single-part
    /// extension.
    /// </summary>
    public DocumentViewType GetDocumentViewType(string fileName)
    {
        var fileExtension = Path.GetExtension(fileName).ToLowerInvariant();

        if (fileExtension == ExplorerConstants.BrowserExtension)
        {
            return DocumentViewType.BrowserDocument;
        }

        if (fileExtension == ExplorerConstants.MarkdownExtension)
        {
            return DocumentViewType.Markdown;
        }

        if (IsFileViewerExtension(fileExtension))
        {
            return DocumentViewType.FileViewer;
        }

        // For both recognized text extensions and unrecognized extensions,
        // return TextDocument. Unrecognized extensions will use the "plaintext" language.
        return DocumentViewType.TextDocument;
    }

    /// <summary>
    /// Gets the text editor language for a file extension.
    /// Queries registered document editor factories for the language mapping.
    /// Returns "plaintext" for empty or unrecognized file extensions.
    /// </summary>
    public string GetTextEditorLanguage(string fileExtension)
    {
        if (string.IsNullOrEmpty(fileExtension))
        {
            return PlaintextLanguage;
        }

        var language = _documentEditorRegistry?.GetLanguageForExtension(fileExtension);
        return language ?? PlaintextLanguage;
    }

    /// <summary>
    /// Determines if a file is recognized (either as a text editor type or a
    /// supported binary type) based on its single-part extension.
    /// </summary>
    public bool IsRecognizedExtension(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        var fileExtension = Path.GetExtension(fileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(fileExtension))
        {
            return false;
        }

        if (fileExtension == ExplorerConstants.BrowserExtension)
        {
            return true;
        }

        if (fileExtension == ExplorerConstants.MarkdownExtension)
        {
            return true;
        }

        if (_documentEditorRegistry?.IsExtensionSupported(fileExtension) == true)
        {
            return true;
        }

        return false;
    }

    // Returns true when the File Viewer registers the extension. Those files open only in the File Viewer, with no
    // text editor offered. The File Viewer's manifest is the one list of its types.
    private bool IsFileViewerExtension(string fileExtension)
    {
        if (_documentEditorRegistry is null)
        {
            return false;
        }

        return _documentEditorRegistry
            .GetFactoriesForExtension(fileExtension)
            .Any(factory => factory.EditorId == BuiltInEditors.FileViewerId);
    }
}
