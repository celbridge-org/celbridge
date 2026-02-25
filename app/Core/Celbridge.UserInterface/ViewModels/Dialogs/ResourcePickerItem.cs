using Celbridge.UserInterface;

namespace Celbridge.UserInterface.ViewModels;

/// <summary>
/// Represents a single file item in the Resource Picker dialog list.
/// </summary>
public class ResourcePickerItem
{
    public IResource Resource { get; }
    public ResourceKey ResourceKey { get; }
    public FileIconDefinition IconDefinition { get; }

    /// <summary>
    /// The full resource key path displayed in the list (e.g. "docs/images/photo.png").
    /// </summary>
    public string DisplayText => ResourceKey.ToString();

    public ResourcePickerItem(IResource resource, ResourceKey resourceKey, FileIconDefinition iconDefinition)
    {
        Resource = resource;
        ResourceKey = resourceKey;
        IconDefinition = iconDefinition;
    }
}
