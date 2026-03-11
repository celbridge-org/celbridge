namespace Celbridge.Documents;

/// <summary>
/// Base class for document editor factories that provides default implementations
/// for common patterns like extension-based CanHandle and default Priority.
/// Factories with custom logic (e.g., CodeEditorFactory) can implement IDocumentEditorFactory directly.
/// </summary>
public abstract class DocumentEditorFactoryBase : IDocumentEditorFactory
{
    public abstract IReadOnlyList<string> SupportedExtensions { get; }

    public virtual int Priority => 0;

    public virtual bool CanHandle(ResourceKey fileResource, string filePath)
    {
        var extension = Path.GetExtension(fileResource.ToString()).ToLowerInvariant();
        return SupportedExtensions.Contains(extension);
    }

    public abstract Result<IDocumentView> CreateDocumentView(ResourceKey fileResource);

    public virtual string? GetLanguageForExtension(string extension) => null;
}
