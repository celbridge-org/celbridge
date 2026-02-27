namespace Celbridge.Documents.Services;

/// <summary>
/// Registry for document editor factories.
/// Manages the mapping between file extensions and factories that create document views.
/// </summary>
public class DocumentEditorRegistry : IDocumentEditorRegistry
{
    private readonly List<IDocumentEditorFactory> _factories = new();
    private readonly Dictionary<string, List<IDocumentEditorFactory>> _extensionToFactories = new();

    /// <summary>
    /// Registers a document editor factory.
    /// </summary>
    public Result RegisterFactory(IDocumentEditorFactory factory)
    {
        Guard.IsNotNull(factory);

        if (factory.SupportedExtensions.Count == 0)
        {
            return Result.Fail("Factory must support at least one extension");
        }

        _factories.Add(factory);

        // Index the factory by each supported extension
        foreach (var extension in factory.SupportedExtensions)
        {
            var normalizedExtension = extension.ToLowerInvariant();

            if (!_extensionToFactories.TryGetValue(normalizedExtension, out var factoryList))
            {
                factoryList = new List<IDocumentEditorFactory>();
                _extensionToFactories[normalizedExtension] = factoryList;
            }

            factoryList.Add(factory);

            // Sort by priority (highest first) so GetFactory can return the first match
            factoryList.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        return Result.Ok();
    }

    /// <summary>
    /// Gets the factory for the specified file resource.
    /// Returns the highest priority factory that can handle the resource.
    /// </summary>
    public Result<IDocumentEditorFactory> GetFactory(ResourceKey fileResource, string filePath)
    {
        var extension = Path.GetExtension(fileResource.ToString()).ToLowerInvariant();

        if (!_extensionToFactories.TryGetValue(extension, out var factoryList))
        {
            return Result<IDocumentEditorFactory>.Fail($"No factory registered for extension: '{extension}'");
        }

        // Find the first factory (sorted by priority) that can handle the resource
        foreach (var factory in factoryList)
        {
            if (factory.CanHandle(fileResource, filePath))
            {
                return Result<IDocumentEditorFactory>.Ok(factory);
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
}
