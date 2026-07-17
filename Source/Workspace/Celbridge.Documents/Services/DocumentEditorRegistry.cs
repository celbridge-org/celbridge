namespace Celbridge.Documents.Services;

/// <summary>
/// Registry for document editor factories.
/// Manages the mapping between file extensions and factories that create document views.
/// </summary>
public class DocumentEditorRegistry : IDocumentEditorRegistry, IDisposable
{
    private bool _disposed;
    private readonly ITextBinarySniffer _textBinarySniffer;
    private readonly List<IDocumentEditorFactory> _factories = new();
    private readonly Dictionary<string, List<IDocumentEditorFactory>> _extensionToFactories = new();
    private readonly Dictionary<string, List<IDocumentEditorFactory>> _filenameToFactories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<EditorInstanceId> _registeredEditorIds = new();
    private readonly Dictionary<EditorInstanceId, IDocumentEditorFactory> _idToFactory = new();

    public DocumentEditorRegistry(ITextBinarySniffer textBinarySniffer)
    {
        _textBinarySniffer = textBinarySniffer;
    }

    public Result RegisterFactory(IDocumentEditorFactory factory)
    {
        Guard.IsNotNull(factory);

        // NSubstitute stubs return null for collection properties that are
        // never explicitly configured, so treat null as empty here rather than
        // pushing the burden to every test setup.
        var supportedExtensions = factory.SupportedExtensions ?? Array.Empty<string>();
        var supportedFilenames = factory.SupportedFilenames ?? Array.Empty<string>();

        var supportsAnything = supportedExtensions.Count > 0
            || supportedFilenames.Count > 0;
        if (!supportsAnything)
        {
            return Result.Fail("Factory must support at least one extension or filename");
        }

        if (!_registeredEditorIds.Add(factory.EditorId))
        {
            return Result.Fail(
                $"Duplicate editor ID '{factory.EditorId}' detected. " +
                $"Skipping registration of '{factory.DisplayName}'. " +
                $"An editor with this ID is already registered as '{_idToFactory[factory.EditorId].DisplayName}'.");
        }

        _idToFactory[factory.EditorId] = factory;
        _factories.Add(factory);

        // Multi-part extensions such as ".document.toml" are indexed as-is. The
        // longest-suffix walk in GetFactory tries the most specific form first.
        foreach (var extension in supportedExtensions)
        {
            var normalizedExtension = extension.ToLowerInvariant();

            if (!_extensionToFactories.TryGetValue(normalizedExtension, out var factoryList))
            {
                factoryList = new List<IDocumentEditorFactory>();
                _extensionToFactories[normalizedExtension] = factoryList;
            }

            factoryList.Add(factory);

            // Sort by priority so GetFactory returns the specialized editor first
            factoryList.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        // Filename matches are tried before any extension match in GetFactory.
        foreach (var filename in supportedFilenames)
        {
            if (!_filenameToFactories.TryGetValue(filename, out var factoryList))
            {
                factoryList = new List<IDocumentEditorFactory>();
                _filenameToFactories[filename] = factoryList;
            }

            factoryList.Add(factory);
            factoryList.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        return Result.Ok();
    }

    public Result<IDocumentEditorFactory> GetFactory(ResourceKey fileResource)
    {
        var candidates = GetFactoriesForResource(fileResource);
        if (candidates.Count == 0)
        {
            return Result<IDocumentEditorFactory>.Fail($"No registered factory can handle resource: '{fileResource}'");
        }
        return Result<IDocumentEditorFactory>.Ok(candidates[0]);
    }

    public IReadOnlyList<IDocumentEditorFactory> GetFactoriesForResource(ResourceKey fileResource)
    {
        // Match order: exact filename first, then multi-part extension suffixes
        // longest first. Dedupe by editor id so a factory registered against
        // both a filename and an extension does not appear twice in the
        // "Open with..." dialog.
        var fileName = fileResource.ResourceName;
        var seenEditorIds = new HashSet<EditorInstanceId>();
        var candidates = new List<IDocumentEditorFactory>();

        if (_filenameToFactories.TryGetValue(fileName, out var byFilename))
        {
            foreach (var factory in byFilename)
            {
                if (factory.CanHandleResource(fileResource)
                    && seenEditorIds.Add(factory.EditorId))
                {
                    candidates.Add(factory);
                }
            }
        }

        var lowerFileName = fileName.ToLowerInvariant();
        foreach (var suffix in GetExtensionSuffixes(lowerFileName))
        {
            if (_extensionToFactories.TryGetValue(suffix, out var factoryList))
            {
                foreach (var factory in factoryList)
                {
                    if (factory.CanHandleResource(fileResource)
                        && seenEditorIds.Add(factory.EditorId))
                    {
                        candidates.Add(factory);
                    }
                }
            }
        }

        return candidates;
    }

    public IReadOnlyList<IDocumentEditorFactory> GetUserPickableFactoriesForResource(ResourceKey fileResource)
    {
        var candidates = GetFactoriesForResource(fileResource)
            .Where(factory => !factory.IsPlaceholder)
            .ToList();

        var extension = Path.GetExtension(fileResource.ResourceName).ToLowerInvariant();
        if (_textBinarySniffer.IsBinaryExtension(extension))
        {
            return candidates;
        }

        // Text-shaped files always get the code editor as a "view as text" option,
        // even if no factory claims the extension. Skip if already in the list.
        var codeEditorResult = GetFactoryById(DocumentConstants.CodeEditorId);
        if (codeEditorResult.IsSuccess
            && !candidates.Any(factory => factory.EditorId == codeEditorResult.Value.EditorId))
        {
            candidates.Add(codeEditorResult.Value);
        }

        return candidates;
    }

    public bool IsExtensionSupported(string fileExtension)
    {
        var normalizedExtension = fileExtension.ToLowerInvariant();
        return _extensionToFactories.ContainsKey(normalizedExtension);
    }

    public IReadOnlyList<IDocumentEditorFactory> GetAllFactories()
    {
        return _factories.AsReadOnly();
    }

    public IReadOnlyList<IDocumentEditorFactory> GetFactoriesForExtension(string fileExtension)
    {
        var normalizedExtension = fileExtension.ToLowerInvariant();

        if (_extensionToFactories.TryGetValue(normalizedExtension, out var factoryList))
        {
            return factoryList.AsReadOnly();
        }

        return [];
    }

    public Result<IDocumentEditorFactory> GetFactoryById(EditorInstanceId editorId)
    {
        if (_idToFactory.TryGetValue(editorId, out var factory))
        {
            return Result<IDocumentEditorFactory>.Ok(factory);
        }

        return Result<IDocumentEditorFactory>.Fail($"No factory found with EditorInstanceId: '{editorId}'");
    }

    public string? GetLanguageForExtension(string fileExtension)
    {
        var normalizedExtension = fileExtension.ToLowerInvariant();

        if (!_extensionToFactories.TryGetValue(normalizedExtension, out var factoryList))
        {
            return null;
        }

        // Factories are sorted by priority, so return the first non-null language
        foreach (var factory in factoryList)
        {
            var language = factory.GetLanguageForExtension(normalizedExtension);
            if (language is not null)
            {
                return language;
            }
        }

        return null;
    }

    public IReadOnlyList<string> GetAllSupportedExtensions()
    {
        return _extensionToFactories.Keys.ToList().AsReadOnly();
    }

    // Yields the extension suffixes of a filename from longest to shortest.
    // "foo.document.toml" produces ".document.toml" then ".toml". "foo.md"
    // produces ".md". "Makefile" produces nothing. A leading dot
    // (".gitignore") is skipped so the file's full name is not treated as
    // an extension.
    private static IEnumerable<string> GetExtensionSuffixes(string fileName)
    {
        int searchFrom = 0;

        if (fileName.Length > 0
            && fileName[0] == '.')
        {
            searchFrom = 1;
        }

        while (searchFrom < fileName.Length)
        {
            int dotIndex = fileName.IndexOf('.', searchFrom);
            if (dotIndex < 0)
            {
                yield break;
            }

            yield return fileName.Substring(dotIndex);
            searchFrom = dotIndex + 1;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var factory in _factories)
        {
            if (factory is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        _factories.Clear();
        _extensionToFactories.Clear();
        _filenameToFactories.Clear();
        _registeredEditorIds.Clear();
        _idToFactory.Clear();
    }
}
