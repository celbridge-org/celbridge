namespace Celbridge.UserInterface.ViewModels;

/// <summary>
/// Represents a single file or folder in the resource picker list.
/// </summary>
public class ResourcePickerItem
{
    public ResourceKey ResourceKey { get; }
    public IconDefinition IconDefinition { get; }

    /// <summary>
    /// The full resource key path displayed in the list (e.g. "docs/images/photo.png").
    /// </summary>
    public string DisplayText { get; }

    /// <summary>
    /// Pre-computed lowercase version of DisplayText for efficient filtering.
    /// </summary>
    public string DisplayTextLower { get; }

    /// <summary>
    /// Whether the resource accepts edits, and the reason when it does not.
    /// </summary>
    public WritableState WritableState { get; }

    /// <summary>
    /// Whether the resource refuses edits. Drives the dimming binding and the
    /// visibility of the read-only tooltip.
    /// </summary>
    public bool IsReadOnly => WritableState != WritableState.Writable;

    /// <summary>
    /// Whether the resource accepts edits. Drives the dimmed-opacity binding for
    /// the icon and display text, which dim when the resource is read-only.
    /// </summary>
    public bool IsWritable => !IsReadOnly;

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
        ResourceKey resourceKey,
        IconDefinition iconDefinition,
        WritableState writableState,
        string? readOnlyMessage = null)
        : this(resourceKey, DisplayTextFor(resourceKey), iconDefinition, writableState, readOnlyMessage)
    {
    }

    private ResourcePickerItem(
        ResourceKey resourceKey,
        string displayText,
        IconDefinition iconDefinition,
        WritableState writableState,
        string? readOnlyMessage)
    {
        ResourceKey = resourceKey;
        IconDefinition = iconDefinition;
        WritableState = writableState;
        DisplayText = displayText;
        DisplayTextLower = DisplayText.ToLowerInvariant();
        ReadOnlyMessage = readOnlyMessage ?? string.Empty;
    }

    /// <summary>
    /// Creates the item for the project folder itself. The item has an empty key and shows the given display text.
    /// </summary>
    public static ResourcePickerItem ForProjectFolder(string displayText, IconDefinition iconDefinition)
    {
        return new ResourcePickerItem(ResourceKey.Empty, displayText, iconDefinition, WritableState.Writable, null);
    }

    // A resource in the project: root shows its bare path. A resource in any other root shows the full "root:path"
    // form, so the list names its root.
    private static string DisplayTextFor(ResourceKey resourceKey)
    {
        if (resourceKey.Root == ResourceKey.DefaultRoot)
        {
            return resourceKey.Path;
        }

        return resourceKey.ToString();
    }

    /// <summary>
    /// Gives the list item its accessible name, since the list reads an item's name from ToString.
    /// </summary>
    public override string ToString() => DisplayText;
}
