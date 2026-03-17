namespace Celbridge.Extensions;

/// <summary>
/// Stores discovered extensions and provides query access to them.
/// </summary>
public class ExtensionRegistry
{
    private List<Extension> _bundledExtensions = [];
    private List<Extension> _projectExtensions = [];

    public void Clear()
    {
        _bundledExtensions.Clear();
        _projectExtensions.Clear();
    }

    public void AddBundledExtension(Extension extension)
    {
        _bundledExtensions.Add(extension);
    }

    public void AddProjectExtension(Extension extension)
    {
        _projectExtensions.Add(extension);
    }

    public IReadOnlyList<Extension> GetAllExtensions()
    {
        var combined = new List<Extension>(_bundledExtensions.Count + _projectExtensions.Count);
        combined.AddRange(_bundledExtensions);
        combined.AddRange(_projectExtensions);
        return combined.AsReadOnly();
    }

    public IReadOnlyList<DocumentContribution> GetAllDocumentEditors()
    {
        return GetAllExtensions()
            .SelectMany(extension => extension.DocumentEditors)
            .ToList()
            .AsReadOnly();
    }
}
