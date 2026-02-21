using System.Reflection;
using System.Text.Json;
using Celbridge.Explorer;

namespace Celbridge.Documents.Services;

public class FileTypeHelper
{
    private const string TextEditorTypesResourceName = "Celbridge.Documents.Assets.DocumentTypes.TextEditorTypes.json";
    private const string FileViewerTypesResourceName = "Celbridge.Documents.Assets.DocumentTypes.FileViewerTypes.json";
    private const string PlaintextLanguage = "plaintext";
    private const string SpreadJSLicense = "ms-appx:///Celbridge.Documents/Web/SpreadJS/lib/license.js";

    private Dictionary<string, string> _extensionToLanguage = new();
    private List<string> _fileViewerExtensions = new();
    private HashSet<string> _binaryFileExtensions = new();

    /// <summary>
    /// Indicates whether the SpreadJS Excel editor is available.
    /// </summary>
    public bool IsSpreadJSAvailable { get; private set; }

    public Result Initialize()
    {
        var loadTextResult = LoadTextEditorTypes();
        if (loadTextResult.IsFailure)
        {
            return loadTextResult;
        }

        var loadWebResult = LoadFileViewerTypes();
        if (loadWebResult.IsFailure)
        {
            return loadWebResult;
        }

        // The SpreadJS Excel editor is only available in Celbridge installer builds.
        IsSpreadJSAvailable = CheckSpreadJSAvailability();

        if (IsSpreadJSAvailable)
        {
            // Only add Excel extension if SpreadJS is available.
            _binaryFileExtensions.Add(ExplorerConstants.ExcelExtension);
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
        if (fileExtension == ExplorerConstants.WebAppExtension)
        {
            return DocumentViewType.WebAppDocument;
        }

        if (fileExtension == ExplorerConstants.MarkdownExtension)
        {
            return DocumentViewType.Markdown;
        }

        if (fileExtension == ExplorerConstants.ExcelExtension)
        {
            // Only return Spreadsheet view type if SpreadJS is available.
            // Otherwise, return UnsupportedFormat so the user is prompted to open with default app.
            return IsSpreadJSAvailable ? DocumentViewType.Spreadsheet : DocumentViewType.UnsupportedFormat;
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
    /// Returns "plaintext" for empty or unrecognized file extensions.
    /// </summary>
    public string GetTextEditorLanguage(string fileExtension)
    {
        if (!string.IsNullOrEmpty(fileExtension) &&
            _extensionToLanguage.TryGetValue(fileExtension, out var language))
        {
            return language;
        }

        return PlaintextLanguage;
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

        // Check for web app extension
        if (fileExtension == ExplorerConstants.WebAppExtension)
        {
            return true;
        }

        // Check if it's a known text editor type
        if (_extensionToLanguage.ContainsKey(fileExtension))
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

    private Result LoadTextEditorTypes()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var stream = assembly.GetManifestResourceStream(TextEditorTypesResourceName);
        if (stream is null)
        {
            return Result.Fail($"Embedded resource not found: {TextEditorTypesResourceName}");
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
            return Result.Fail($"An exception occurred when reading content of embedded resource: {TextEditorTypesResourceName}")
                .WithException(ex);
        }

        try
        {
            // Deserialize the JSON into a dictionary mapping file extensions to languages
            var dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dictionary is null)
            {
                return Result.Fail($"Failed to deserialize embedded resource: {TextEditorTypesResourceName}");
            }

            _extensionToLanguage.ReplaceWith(dictionary);
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when deserializing embedded resource: {TextEditorTypesResourceName}")
                .WithException(ex);
        }

        return Result.Ok();
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

    /// <summary>
    /// Checks if the SpreadJS license file is available in the app package.
    /// </summary>
    private static bool CheckSpreadJSAvailability()
    {
        try
        {
            var uri = new Uri(SpreadJSLicense);
            // Use synchronous wait since this is called during initialization
            var task = StorageFile.GetFileFromApplicationUriAsync(uri).AsTask();
            task.Wait();
            return true;
        }
        catch
        {
            // The SpreadJS license file is not present
            return false;
        }
    }
}
