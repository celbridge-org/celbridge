namespace Celbridge.Documents.Services;

/// <summary>
/// Registry for document editor factories.
/// Manages the mapping between file extensions and factories that create document views.
/// </summary>
public class DocumentEditorRegistry : IDocumentEditorRegistry, IDisposable
{
    private bool _disposed;
    private readonly List<IDocumentEditorFactory> _factories = new();
    private readonly Dictionary<string, List<IDocumentEditorFactory>> _extensionToFactories = new();
    private readonly Dictionary<string, List<IDocumentEditorFactory>> _filenameToFactories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<DocumentEditorId> _registeredEditorIds = new();
    private readonly Dictionary<DocumentEditorId, IDocumentEditorFactory> _idToFactory = new();

    /// <summary>
    /// Registers a document editor factory.
    /// </summary>
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

        // Index the factory by each supported extension.
        // Multi-part extensions such as ".project.cel" are indexed as-is; the
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

        // Index the factory by each supported exact filename. Filename matches
        // are tried before any extension match in GetFactory.
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

    /// <summary>
    /// Gets the factory for the specified file resource.
    /// Returns the highest priority factory that can handle the resource.
    /// </summary>
    public Result<IDocumentEditorFactory> GetFactory(ResourceKey fileResource, string filePath)
    {
        // Use ResourceKey.ResourceName directly rather than Path.GetFileName on
        // the key's string form. Path.GetFileName treats the "project:" prefix
        // inconsistently across platforms (volume separator on Windows), so it
        // would split "project:package.toml" differently from a real path.
        var fileName = fileResource.ResourceName;

        // 1. Try exact-filename match first. A factory that claims "package.toml"
        //    by filename wins over a generic ".toml" extension factory.
        if (_filenameToFactories.TryGetValue(fileName, out var byFilename))
        {
            foreach (var factory in byFilename)
            {
                if (factory.CanHandleResource(fileResource, filePath))
                {
                    return Result<IDocumentEditorFactory>.Ok(factory);
                }
            }
        }

        // 2. Try multi-part extension suffixes from longest to shortest, so a
        //    ".project.cel" factory beats a generic ".cel" factory on the same file.
        var lowerFileName = fileName.ToLowerInvariant();
        foreach (var suffix in GetExtensionSuffixes(lowerFileName))
        {
            if (_extensionToFactories.TryGetValue(suffix, out var factoryList))
            {
                foreach (var factory in factoryList)
                {
                    if (factory.CanHandleResource(fileResource, filePath))
                    {
                        return Result<IDocumentEditorFactory>.Ok(factory);
                    }
                }
            }
        }

        return Result<IDocumentEditorFactory>.Fail($"No registered factory can handle resource: '{fileResource}'");
    }

    /// <summary>
    /// Checks if any registered factory can handle the specified extension.
    /// </summary>
    public bool IsExtensionSupported(string fileExtension)
    {
        var normalizedExtension = fileExtension.ToLowerInvariant();
        return _extensionToFactories.ContainsKey(normalizedExtension);
    }

    /// <summary>
    /// Gets all registered factories.
    /// </summary>
    public IReadOnlyList<IDocumentEditorFactory> GetAllFactories()
    {
        return _factories.AsReadOnly();
    }

    /// <summary>
    /// Gets all factories that can handle the specified extension, sorted by priority.
    /// </summary>
    public IReadOnlyList<IDocumentEditorFactory> GetFactoriesForFileExtension(string fileExtension)
    {
        var normalizedExtension = fileExtension.ToLowerInvariant();

        if (_extensionToFactories.TryGetValue(normalizedExtension, out var factoryList))
        {
            return factoryList.AsReadOnly();
        }

        return [];
    }

    /// <summary>
    /// Gets a factory by its DocumentEditorId.
    /// </summary>
    public Result<IDocumentEditorFactory> GetFactoryById(DocumentEditorId documentEditorId)
    {
        if (_idToFactory.TryGetValue(documentEditorId, out var factory))
        {
            return Result<IDocumentEditorFactory>.Ok(factory);
        }

        return Result<IDocumentEditorFactory>.Fail($"No factory found with DocumentEditorId: '{documentEditorId}'");
    }

    /// <summary>
    /// Gets the editor language identifier for the specified file extension.
    /// Queries registered factories in priority order and returns the first non-null result.
    /// </summary>
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

    /// <summary>
    /// Gets all file extensions supported by registered factories.
    /// </summary>
    public IReadOnlyList<string> GetAllSupportedExtensions()
    {
        return _extensionToFactories.Keys.ToList().AsReadOnly();
    }

    // Yields the extension suffixes of a filename from longest to shortest.
    // "foo.project.cel" produces ".project.cel" then ".cel"; "foo.md" produces
    // ".md"; "Makefile" produces nothing. A leading dot (".gitignore") is
    // skipped so the file's full name is not treated as an extension.
    private static IEnumerable<string> GetExtensionSuffixes(string fileName)
    {
        int searchFrom = 0;

        // Skip a leading '.' on dotfiles so the first yielded suffix is anchored
        // on an interior dot rather than the leading one.
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

        // Dispose all registered factories that implement IDisposable
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
