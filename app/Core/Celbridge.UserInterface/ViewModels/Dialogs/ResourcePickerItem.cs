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
    public string DisplayText { get; }

    /// <summary>
    /// Pre-computed lowercase version of DisplayText for efficient filtering.
    /// </summary>
    public string DisplayTextLower { get; }

    public ResourcePickerItem(IResource resource, ResourceKey resourceKey, FileIconDefinition iconDefinition)
    {
        Resource = resource;
        ResourceKey = resourceKey;
        IconDefinition = iconDefinition;
        DisplayText = resourceKey.ToString();
        DisplayTextLower = DisplayText.ToLowerInvariant();
    }
}
