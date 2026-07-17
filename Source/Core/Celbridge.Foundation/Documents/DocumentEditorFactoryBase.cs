namespace Celbridge.Documents;

/// <summary>
/// Base class for document editor factories, providing default implementations of
/// filename and extension matching.
/// </summary>
public abstract class DocumentEditorFactoryBase : IDocumentEditorFactory
{
    public abstract EditorInstanceId EditorId { get; }

    public abstract string DisplayName { get; }

    public abstract IReadOnlyList<string> SupportedExtensions { get; }

    public virtual IReadOnlyList<string> SupportedFilenames { get; } = Array.Empty<string>();

    public virtual bool IsPlaceholder => false;

    public virtual bool IsUtility => false;

    public virtual bool CanHandleResource(ResourceKey fileResource)
    {
        var fileName = Path.GetFileName(fileResource.ToString());

        foreach (var supportedFilename in SupportedFilenames)
        {
            if (string.Equals(fileName, supportedFilename, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var lowerFileName = fileName.ToLowerInvariant();
        foreach (var supportedExtension in SupportedExtensions)
        {
            if (lowerFileName.EndsWith(supportedExtension, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // Implementations must set view.EditorId = EditorId on the returned view.
    public abstract Result<IDocumentView> CreateDocumentView(ResourceKey fileResource);

    public virtual string? GetLanguageForExtension(string extension) => null;
}
