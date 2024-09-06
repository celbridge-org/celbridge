using Newtonsoft.Json;
using System.Reflection;

namespace Celbridge.Documents.Services;

public class FileTypeHelper
{
    private const string TextDocumentTypesResourceName = "Celbridge.Documents.Assets.DocumentTypes.TextDocumentTypes.json";
    private const string WebViewerTypesResourceName = "Celbridge.Documents.Assets.DocumentTypes.WebViewerTypes.json";

    // Create a new dictionary to map file extensions to language codes
    private Dictionary<string, string> _textDocumentTypes = new();
    private List<string> _webViewerTypes = new();

    public Result Initialize()
    {
        var loadTextResult = LoadTextDocumentTypes();
        if (loadTextResult.IsFailure)
        {
            return loadTextResult;
        }

        var loadWebResult = LoadWebViewerTypes();
        if (loadWebResult.IsFailure)
        {
            return loadWebResult;
        }

        return Result.Ok();
    }

    public DocumentViewType GetDocumentViewType(string fileExtension)
    {
        var documentLanguage = GetDocumentLanguage(fileExtension);
        if (!string.IsNullOrEmpty(documentLanguage))
        {
            return DocumentViewType.TextDocument;
        }

        if (IsWebViewerFile(fileExtension))
        {
            return DocumentViewType.WebViewer;
        }

        if (fileExtension == ".web")
        {
            return DocumentViewType.WebDocument;
        }

        return DocumentViewType.DefaultDocument;
    }

    public string GetDocumentLanguage(string fileExtension)
    {
        if (string.IsNullOrEmpty(fileExtension))
        {
            return string.Empty;
        }

        if (_textDocumentTypes.TryGetValue(fileExtension,out var language))
        {
            return language;
        }

        return string.Empty;
    }

    public bool IsWebViewerFile(string fileExtension)
    {
        return _webViewerTypes.Contains(fileExtension);
    }

    private Result LoadTextDocumentTypes()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var stream = assembly.GetManifestResourceStream(TextDocumentTypesResourceName);
        if (stream is null)
        {
            return Result.Fail($"Embedded resource not found: {TextDocumentTypesResourceName}");
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
            return Result.Fail(ex, $"An exception occurred when reading content of embedded resource: {TextDocumentTypesResourceName}");
        }

        try
        {
            // Deserialize the JSON into a dictionary of language codes to file extensions
            var languageToExtensions = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(json);

            // Loop through the deserialized data and populate the new dictionary
            foreach (var entry in languageToExtensions!)
            {
                string language = entry.Key;
                List<string> extensions = entry.Value;

                foreach (string extension in extensions)
                {
                    if (_textDocumentTypes.TryGetValue(extension, out var existingValue))
                    {
                        return Result.Fail($"Failed to map extension '{extension}' to language '{language}' because it is already mapped to language '{existingValue}'.");
                    }

                    _textDocumentTypes[extension] = language;
                }
            }
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, $"An exception occurred when deserializing embedded resource: {TextDocumentTypesResourceName}");
        }

        return Result.Ok();
    }

    private Result LoadWebViewerTypes()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var stream = assembly.GetManifestResourceStream(WebViewerTypesResourceName);
        if (stream is null)
        {
            return Result.Fail($"Embedded resource not found: {WebViewerTypesResourceName}");
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
            return Result.Fail(ex, $"An exception occurred when reading content of embedded resource: {WebViewerTypesResourceName}");
        }

        try
        {
            // Deserialize the JSON into a list of file extensions
            var fileExtensions = JsonConvert.DeserializeObject<List<string>>(json);

            _webViewerTypes.ReplaceWith(fileExtensions);
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, $"An exception occurred when deserializing embedded resource: {WebViewerTypesResourceName}");
        }

        return Result.Ok();
    }

}
