using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// The project-level state of a feature flag: inherit the application default, or pin it on or off.
/// </summary>
public enum FeatureFlagSelection
{
    Default = 0,
    On = 1,
    Off = 2
}

/// <summary>
/// The descriptive fields of one feature flag row, resolved from the catalog and the project config.
/// </summary>
public sealed record FeatureFlagItemInfo
{
    /// <summary>
    /// The feature flag key written to the project's features table.
    /// </summary>
    public string FlagName { get; init; } = string.Empty;

    /// <summary>
    /// Localized title of the flag.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Localized description of what the flag does.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// The value the flag resolves to when the project inherits the default, shown on the Default option.
    /// </summary>
    public bool ApplicationValue { get; init; }

    /// <summary>
    /// The project's current state for the flag.
    /// </summary>
    public FeatureFlagSelection Selection { get; init; }
}

/// <summary>
/// One feature flag on the Feature Flags section, with a tri-state control that pins its project override
/// on or off, or clears it to inherit the application default. Changing it writes straight through to the
/// .celbridge file.
/// </summary>
public partial class FeatureFlagItemViewModel : ObservableObject
{
    private readonly FeatureFlagItemInfo _info;
    private readonly Action<string, FeatureFlagSelection> _setSelection;

    private bool _initialized;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEffectivelyEnabled))]
    private int _selectedIndex;

    public FeatureFlagItemViewModel(FeatureFlagItemInfo info, Action<string, FeatureFlagSelection> setSelection)
    {
        _info = info;
        _setSelection = setSelection;

        StateOptions = new List<string>
        {
            ProjectSettingsLabels.FeatureFlagDefault(info.ApplicationValue),
            ProjectSettingsLabels.FeatureFlagOn,
            ProjectSettingsLabels.FeatureFlagOff,
        };

        SelectedIndex = (int)info.Selection;
        _initialized = true;
    }

    public string Title => _info.Title;

    public string Description => _info.Description;

    /// <summary>
    /// The three selectable states shown in the control: the resolved default, on, and off.
    /// </summary>
    public IReadOnlyList<string> StateOptions { get; }

    /// <summary>
    /// The flag's resolved state: an explicit On or Off selection, otherwise the inherited application
    /// default. Drives the header dimming so its state reads at a glance while the card is collapsed.
    /// </summary>
    public bool IsEffectivelyEnabled
    {
        get
        {
            var selection = (FeatureFlagSelection)SelectedIndex;
            if (selection == FeatureFlagSelection.On)
            {
                return true;
            }
            if (selection == FeatureFlagSelection.Off)
            {
                return false;
            }

            return _info.ApplicationValue;
        }
    }

    partial void OnSelectedIndexChanged(int value)
    {
        if (_initialized)
        {
            _setSelection(_info.FlagName, (FeatureFlagSelection)value);
        }
    }
}
