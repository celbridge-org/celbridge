namespace Celbridge.Documents;

/// <summary>
/// Base class for document editor factories that provides default implementations
/// for common patterns like extension-based CanHandleResource and default Priority.
/// Factories with custom logic can implement IDocumentEditorFactory directly.
/// </summary>
public abstract class DocumentEditorFactoryBase : IDocumentEditorFactory
{
    public abstract DocumentEditorId EditorId { get; }

    public abstract string DisplayName { get; }

    public abstract IReadOnlyList<string> SupportedExtensions { get; }

    public virtual EditorPriority Priority => EditorPriority.Specialized;

    public virtual bool CanHandleResource(ResourceKey fileResource, string filePath)
    {
        var extension = Path.GetExtension(fileResource.ToString()).ToLowerInvariant();
        return SupportedExtensions.Contains(extension);
    }

    public abstract Result<IDocumentView> CreateDocumentView(ResourceKey fileResource);

    public virtual string? GetLanguageForExtension(string extension) => null;
}
