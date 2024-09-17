using Newtonsoft.Json;
using System.Reflection;

namespace Celbridge.Documents.Services;

public class FileTypeHelper
{
    private const string TextEditorTypesResourceName = "Celbridge.Documents.Assets.DocumentTypes.TextEditorTypes.json";
    private const string FileViewerTypesResourceName = "Celbridge.Documents.Assets.DocumentTypes.FileViewerTypes.json";

    private Dictionary<string, string> _extensionToLanguage = new();
    private List<string> _fileViewerExtensions = new();

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

        return Result.Ok();
    }

    public DocumentViewType GetDocumentViewType(string fileExtension)
    {
        var documentLanguage = GetTextEditorLanguage(fileExtension);
        if (!string.IsNullOrEmpty(documentLanguage))
        {
            return DocumentViewType.TextDocument;
        }

        if (IsWebViewerFile(fileExtension))
        {
            return DocumentViewType.FileViewer;
        }

        if (fileExtension == ".web")
        {
            return DocumentViewType.WebPageDocument;
        }

        return DocumentViewType.Unsupported;
    }

    public string GetTextEditorLanguage(string fileExtension)
    {
        if (string.IsNullOrEmpty(fileExtension))
        {
            return string.Empty;
        }

        if (_extensionToLanguage.TryGetValue(fileExtension,out var language))
        {
            return language;
        }

        return string.Empty;
    }

    public bool IsWebViewerFile(string fileExtension)
    {
        return _fileViewerExtensions.Contains(fileExtension);
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
            return Result.Fail(ex, $"An exception occurred when reading content of embedded resource: {TextEditorTypesResourceName}");
        }

        try
        {
            // Deserialize the JSON into a dictionary mapping file extensions to languages
            var dictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            if (dictionary is null)
            {
                return Result.Fail($"Failed to deserialize embedded resource: {TextEditorTypesResourceName}");
            }

            _extensionToLanguage.ReplaceWith(dictionary);
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, $"An exception occurred when deserializing embedded resource: {TextEditorTypesResourceName}");
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
            return Result.Fail(ex, $"An exception occurred when reading content of embedded resource: {FileViewerTypesResourceName}");
        }

        try
        {
            // Deserialize the JSON into a list of file extensions
            var fileExtensions = JsonConvert.DeserializeObject<List<string>>(json);

            _fileViewerExtensions.ReplaceWith(fileExtensions);
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, $"An exception occurred when deserializing embedded resource: {FileViewerTypesResourceName}");
        }

        return Result.Ok();
    }

}
