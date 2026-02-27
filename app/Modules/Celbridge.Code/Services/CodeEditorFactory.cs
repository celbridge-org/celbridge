using System.Reflection;
using System.Text.Json;
using Celbridge.Code.Views;
using Celbridge.Documents;
using Celbridge.Documents.Views;

namespace Celbridge.Code.Services;

/// <summary>
/// Factory for creating code/text editor document views.
/// Handles text files using the Monaco editor on Windows, or TextBox fallback on other platforms.
/// </summary>
public class CodeEditorFactory : IDocumentEditorFactory
{
    private const string TextEditorTypesResourceName = "Celbridge.Code.Assets.TextEditorTypes.json";
    private const string PlaintextLanguage = "plaintext";

    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, string> _extensionToLanguage;

    public IReadOnlyList<string> SupportedExtensions { get; }

    public int Priority => 0;

    public CodeEditorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

        // Load the extension to language mappings from the embedded JSON resource.
        _extensionToLanguage = LoadTextEditorTypes();

        // The supported extensions are the keys from the loaded mappings.
        SupportedExtensions = _extensionToLanguage.Keys.ToList().AsReadOnly();
    }

    public bool CanHandle(ResourceKey fileResource, string filePath)
    {
        var extension = Path.GetExtension(fileResource.ToString()).ToLowerInvariant();
        return _extensionToLanguage.ContainsKey(extension);
    }

    public Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
#if WINDOWS
        var view = _serviceProvider.GetRequiredService<TextEditorDocumentView>();
        return Result<IDocumentView>.Ok(view);
#else
        var view = _serviceProvider.GetRequiredService<TextBoxDocumentView>();
        return Result<IDocumentView>.Ok(view);
#endif
    }

    public string? GetLanguageForExtension(string extension)
    {
        var normalizedExtension = extension.ToLowerInvariant();

        if (_extensionToLanguage.TryGetValue(normalizedExtension, out var language))
        {
            return language;
        }

        return null;
    }

    private static Dictionary<string, string> LoadTextEditorTypes()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var stream = assembly.GetManifestResourceStream(TextEditorTypesResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded resource not found: {TextEditorTypesResourceName}");
        }

        using (stream)
        using (var reader = new StreamReader(stream))
        {
            var json = reader.ReadToEnd();
            var dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            if (dictionary is null)
            {
                throw new InvalidOperationException($"Failed to deserialize embedded resource: {TextEditorTypesResourceName}");
            }

            return dictionary;
        }
    }
}
