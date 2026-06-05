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

    /// <summary>
    /// The writable state of the underlying resource, sourced from the
    /// ProjectTreeBuilder-populated cache on IResource.
    /// </summary>
    public WritableState WritableState => Resource.WritableState;

    /// <summary>
    /// Whether the resource refuses edits. Drives the dimming binding and the
    /// visibility of the read-only tooltip.
    /// </summary>
    public bool IsReadOnly => WritableState != WritableState.Writable;

    /// <summary>
    /// Opacity for the icon and display text. Dimmed when read-only.
    /// </summary>
    public double NameOpacity => IsReadOnly ? 0.5 : 1.0;

    /// <summary>
    /// Localised explanation of why the resource is read-only. Empty when
    /// writable. Drives AutomationProperties.HelpText so screen readers carry
    /// the same signal as the visual dimming.
    /// </summary>
    public string ReadOnlyMessage { get; }

    /// <summary>
    /// The tooltip shown when the user hovers the item. Non-editable items show
    /// the read-only reason; editable items have no tooltip (returns null so
    /// the tooltip element does not render).
    /// </summary>
    public string? TooltipText => string.IsNullOrEmpty(ReadOnlyMessage)
        ? null
        : ReadOnlyMessage;

    public ResourcePickerItem(
        IResource resource,
        ResourceKey resourceKey,
        FileIconDefinition iconDefinition,
        string? readOnlyMessage = null)
    {
        Resource = resource;
        ResourceKey = resourceKey;
        IconDefinition = iconDefinition;
        // Display text uses the bare path for project-rooted resources (cleaner
        // for the picker UI) and falls back to the full "root:path" form for
        // non-default roots so the root is visible when it matters.
        DisplayText = resourceKey.Root == ResourceKey.DefaultRoot
            ? resourceKey.Path
            : resourceKey.ToString();
        DisplayTextLower = DisplayText.ToLowerInvariant();
        ReadOnlyMessage = readOnlyMessage ?? string.Empty;
    }
}
