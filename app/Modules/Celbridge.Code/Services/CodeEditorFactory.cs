using System.Reflection;
using System.Text.Json;
using Celbridge.Code.Views;
using Celbridge.Documents;
using Celbridge.Documents.Views;
using Celbridge.Logging;

namespace Celbridge.Code.Services;

/// <summary>
/// Factory for creating code/text editor document views.
/// Handles text files using the Monaco editor on Windows, or TextBox fallback on other platforms.
/// Maintains a pre-warmed editor instance for faster document opening.
/// </summary>
public class CodeEditorFactory : IDocumentEditorFactory, IDisposable
{
    private const string CodeEditorTypesResourceName = "Celbridge.Code.Assets.CodeEditorTypes.json";
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, string> _extensionToLanguage;

#if WINDOWS
    private readonly ILogger<CodeEditorFactory> _logger;
    private readonly Lock _lock = new();
    private CodeEditorDocumentView? _warmInstance;
    private bool _isDisposed;
#endif

    public IReadOnlyList<string> SupportedExtensions { get; }

    public int Priority => 0;

    public CodeEditorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

#if WINDOWS
        _logger = ServiceLocator.AcquireService<ILogger<CodeEditorFactory>>();
#endif

        // Load the extension to language mappings from the embedded JSON resource.
        _extensionToLanguage = LoadCodeEditorTypes();

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
        CodeEditorDocumentView? view = null;
        bool shouldStartWarming = false;

        lock (_lock)
        {
            if (_isDisposed)
            {
                return Result<IDocumentView>.Fail("CodeEditorFactory has been disposed");
            }

            if (_warmInstance is not null)
            {
                // Use the pre-warmed instance
                view = _warmInstance;
                _warmInstance = null;
                shouldStartWarming = true;
            }
        }

        if (view is null)
        {
            // No warm instance available, create a new one
            view = _serviceProvider.GetRequiredService<CodeEditorDocumentView>();
            shouldStartWarming = true;
        }

        // Start warming the next instance in the background
        if (shouldStartWarming)
        {
            _ = WarmUpInstanceAsync();
        }

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

    private static Dictionary<string, string> LoadCodeEditorTypes()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var stream = assembly.GetManifestResourceStream(CodeEditorTypesResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded resource not found: {CodeEditorTypesResourceName}");
        }

        using (stream)
        using (var reader = new StreamReader(stream))
        {
            var json = reader.ReadToEnd();
            var dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            if (dictionary is null)
            {
                throw new InvalidOperationException($"Failed to deserialize embedded resource: {CodeEditorTypesResourceName}");
            }

            return dictionary;
        }
    }

#if WINDOWS
    private async Task WarmUpInstanceAsync()
    {
        try
        {
            var view = _serviceProvider.GetRequiredService<CodeEditorDocumentView>();
            var preWarmResult = await view.PreWarmAsync();

            if (preWarmResult.IsFailure)
            {
                _logger.LogWarning(preWarmResult, "Failed to pre-warm CodeEditorDocumentView instance");
                return;
            }

            lock (_lock)
            {
                if (_isDisposed)
                {
                    // Factory was disposed while warming, clean up the view
                    _ = view.PrepareToClose();
                    return;
                }

                if (_warmInstance is not null)
                {
                    // Another instance was warmed in the meantime, discard this one
                    _ = view.PrepareToClose();
                    return;
                }

                _warmInstance = view;
            }

            _logger.LogDebug("Pre-warmed CodeEditorDocumentView instance is ready");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "An exception occurred while pre-warming CodeEditorDocumentView instance");
        }
    }

    public void Dispose()
    {
        CodeEditorDocumentView? viewToCleanup = null;

        lock (_lock)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            viewToCleanup = _warmInstance;
            _warmInstance = null;
        }

        if (viewToCleanup is not null)
        {
            _ = viewToCleanup.PrepareToClose();
        }
    }
#else
    public void Dispose()
    {
        // No cleanup needed on non-Windows platforms
    }
#endif
}
