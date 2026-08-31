using Celbridge.Projects;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// One document shortcut as its settings card and its Utility Rail button present it: the resource the
/// button opens as a document, and an optional icon named from the bundled icon set.
/// </summary>
public partial class DocumentShortcutViewModel : ObservableObject
{
    // The areas a shortcut can open into, in the order the picker lists them. The Utility Panel is absent
    // because it holds no document tabs.
    private static readonly List<WorkspaceArea> SelectableAreas =
    [
        WorkspaceArea.Main,
        WorkspaceArea.Bottom,
        WorkspaceArea.Side,
    ];

    private readonly IIconService _iconService;
    private readonly Func<ResourceKey, bool> _resourceExists;

    private IReadOnlyList<string>? _areaOptions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(IsResourceInvalid))]
    [NotifyPropertyChangedFor(nameof(IsResourceMissing))]
    private string _resource = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconName))]
    [NotifyPropertyChangedFor(nameof(IsIconUnknown))]
    private string _icon = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedAreaIndex))]
    private WorkspaceArea _area = WorkspaceArea.Main;

    /// <summary>
    /// The text the collapsed card shows. A shortcut is identified by the file it opens, so that is its
    /// name; text that is not a resource key is shown as typed, so a mistake is visible in the header.
    /// </summary>
    public string DisplayName
    {
        get
        {
            var resourceText = Resource.Trim();
            if (string.IsNullOrEmpty(resourceText))
            {
                return ProjectSettingsLabels.ShortcutUntitled;
            }

            if (!ResourceKey.TryCreate(resourceText, out var fileResource))
            {
                return resourceText;
            }

            return fileResource.ResourceName;
        }
    }

    /// <summary>
    /// The icon the card and the rail button draw, which is the default document icon while the shortcut
    /// names none.
    /// </summary>
    public string IconName => DocumentShortcutIcon.Resolve(_iconService, Icon);

    /// <summary>
    /// True when the shortcut names something that is not a resource key, so no button can be built for
    /// it. A blank resource is unconfigured rather than wrong, so it does not report as invalid.
    /// </summary>
    public bool IsResourceInvalid
    {
        get
        {
            var resourceText = Resource.Trim();
            if (string.IsNullOrEmpty(resourceText))
            {
                return false;
            }

            return !ResourceKey.IsValidKey(resourceText);
        }
    }

    /// <summary>
    /// True when the shortcut names a well-formed resource key that the project does not hold, so the
    /// card can say so. The button is still built, the file being one the project may yet gain.
    /// </summary>
    public bool IsResourceMissing
    {
        get
        {
            var resourceText = Resource.Trim();
            if (!ResourceKey.TryCreate(resourceText, out var fileResource))
            {
                return false;
            }

            return !_resourceExists(fileResource);
        }
    }

    /// <summary>
    /// True when the named icon is not one the bundled set carries, so the card can say so. The button
    /// still draws, the icon service resolving an unknown name to a fallback glyph.
    /// </summary>
    public bool IsIconUnknown
    {
        get
        {
            var iconText = Icon.Trim();
            if (string.IsNullOrEmpty(iconText))
            {
                return false;
            }

            return !_iconService.TryGetGlyph(iconText, out _);
        }
    }

    /// <summary>
    /// The areas the picker offers, in SelectableAreas order. Built on first read, because the labels are
    /// localized and the view models are also built outside the running application.
    /// </summary>
    public IReadOnlyList<string> AreaOptions =>
        _areaOptions ??= SelectableAreas.Select(ProjectSettingsLabels.WorkspaceAreaName).ToList();

    /// <summary>
    /// The picker's selected row, over the area the shortcut holds.
    /// </summary>
    public int SelectedAreaIndex
    {
        get
        {
            var index = SelectableAreas.IndexOf(Area);

            // An area the picker does not offer shows as the main area.
            return index < 0 ? 0 : index;
        }
        set
        {
            // A picker with no selection reports -1. Ignoring that leaves the area the shortcut holds
            // rather than clearing it to the default.
            if (value < 0
                || value >= SelectableAreas.Count)
            {
                return;
            }

            Area = SelectableAreas[value];
        }
    }

    public string AreaLabel => ProjectSettingsLabels.ShortcutAreaLabel;
    public string AreaHint => ProjectSettingsLabels.ShortcutAreaHint;
    public string ResourceLabel => ProjectSettingsLabels.ShortcutResourceLabel;
    public string ResourcePlaceholder => ProjectSettingsLabels.ShortcutResourcePlaceholder;
    public string ResourceHint => ProjectSettingsLabels.ShortcutResourceHint;
    public string InvalidResourceText => ProjectSettingsLabels.ShortcutInvalidResource;
    public string MissingResourceText => ProjectSettingsLabels.ShortcutMissingResource;
    public string IconLabel => ProjectSettingsLabels.ShortcutIconLabel;
    public string IconPlaceholder => ProjectSettingsLabels.ShortcutIconPlaceholder;
    public string IconHint => ProjectSettingsLabels.ShortcutIconHint;
    public string UnknownIconText => ProjectSettingsLabels.ShortcutUnknownIcon;

    public DocumentShortcutViewModel(IIconService iconService, Func<ResourceKey, bool> resourceExists)
    {
        _iconService = iconService;
        _resourceExists = resourceExists;
    }

    /// <summary>
    /// The config entry for this shortcut.
    /// </summary>
    public DocumentShortcut ToDocumentShortcut()
    {
        return new DocumentShortcut
        {
            Resource = Resource.Trim(),
            Icon = Icon.Trim(),
            Area = Area
        };
    }
}
