using System.Reflection;
using System.Text.Json;
using Celbridge.Explorer;

namespace Celbridge.Documents.Services;

public class FileTypeHelper
{
    private const string FileViewerTypesResourceName = "Celbridge.Documents.Assets.DocumentTypes.FileViewerTypes.json";
    private const string PlaintextLanguage = "plaintext";

    private List<string> _fileViewerExtensions = new();
    private HashSet<string> _binaryFileExtensions = new();

    private IDocumentEditorRegistry? _documentEditorRegistry;

    /// <summary>
    /// Sets the document editor registry for querying language mappings and supported extensions.
    /// This must be called after factories are registered.
    /// </summary>
    public void SetDocumentEditorRegistry(IDocumentEditorRegistry registry)
    {
        _documentEditorRegistry = registry;
    }

    public Result Initialize()
    {
        var loadWebResult = LoadFileViewerTypes();
        if (loadWebResult.IsFailure)
        {
            return loadWebResult;
        }

        // Initialize the set of supported binary file extensions.
        // These are binary formats that have specific file viewer support.
        foreach (var ext in _fileViewerExtensions)
        {
            _binaryFileExtensions.Add(ext);
        }

        return Result.Ok();
    }

    /// <summary>
    /// Gets the document view type based on the file extension.
    /// </summary>
    public DocumentViewType GetDocumentViewType(string fileExtension)
    {
        if (fileExtension == ExplorerConstants.WebViewExtension)
        {
            return DocumentViewType.WebViewDocument;
        }

        if (fileExtension == ExplorerConstants.MarkdownExtension)
        {
            return DocumentViewType.Markdown;
        }

        if (IsWebViewerFile(fileExtension))
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

    public bool IsWebViewerFile(string fileExtension)
    {
        return _fileViewerExtensions.Contains(fileExtension);
    }

    /// <summary>
    /// Checks if a file extension corresponds to a supported binary format.
    /// Supported binary formats include spreadsheets (.xlsx) and file viewer types (images, audio, video, etc.).
    /// </summary>
    public bool IsSupportedBinaryExtension(string fileExtension)
    {
        return _binaryFileExtensions.Contains(fileExtension);
    }

    /// <summary>
    /// Determines if a file extension is recognized (either as a text editor type or a supported binary type).
    /// </summary>
    public bool IsRecognizedExtension(string fileExtension)
    {
        if (string.IsNullOrEmpty(fileExtension))
        {
            return false;
        }

        // Check for web view extension
        if (fileExtension == ExplorerConstants.WebViewExtension)
        {
            return true;
        }

        // Check for markdown extension (handled by the WYSIWYG editor)
        if (fileExtension == ExplorerConstants.MarkdownExtension)
        {
            return true;
        }

        // Check if it's a known text editor type (via registered factories)
        if (_documentEditorRegistry?.IsExtensionSupported(fileExtension) == true)
        {
            return true;
        }

        // Check if it's a supported binary type
        if (_binaryFileExtensions.Contains(fileExtension))
        {
            return true;
        }

        return false;
    }

    private Result LoadFileViewerTypes()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var stream = assembly.GetManifestResourceStream(FileViewerTypesResourceName);
        if (stream is null)
        {
            return Result.Fail($"Embedded resource not found: {FileViewerTypesResourceName}");
        }

        var json = string.Empty;
        try
        {
            using (stream)
            using (StreamReader reader = new StreamReader(stream))
            {
                json = reader.ReadToEnd();
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when reading content of embedded resource: {FileViewerTypesResourceName}")
                .WithException(ex);
        }

        try
        {
            // Deserialize the JSON into a list of file extensions
            var fileExtensions = JsonSerializer.Deserialize<List<string>>(json);

            _fileViewerExtensions.ReplaceWith(fileExtensions);
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when deserializing embedded resource: {FileViewerTypesResourceName}")
                .WithException(ex);
        }

        return Result.Ok();
    }
}
