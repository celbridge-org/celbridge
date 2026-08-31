namespace Celbridge.UserInterface.ViewModels;

/// <summary>
/// One icon in the Icon Picker dialog list: the prefixed name the field is filled with, and the text the
/// search box is matched against.
/// </summary>
public class IconPickerItem
{
    /// <summary>
    /// The prefixed icon name shown in the list and returned to the caller (e.g. "bs-journal-text").
    /// </summary>
    public string IconName { get; }

    /// <summary>
    /// Pre-computed lowercase version of IconName for efficient filtering.
    /// </summary>
    public string IconNameLower { get; }

    /// <summary>
    /// Pre-computed lowercase text holding every keyword the icon can be searched by, for efficient
    /// filtering. An icon with no keywords is reachable by name alone.
    /// </summary>
    public string KeywordTextLower { get; }

    public IconPickerItem(IconCatalogEntry catalogEntry)
    {
        IconName = catalogEntry.IconName;
        IconNameLower = IconName.ToLowerInvariant();
        KeywordTextLower = string.Join(' ', catalogEntry.Keywords).ToLowerInvariant();
    }
}
