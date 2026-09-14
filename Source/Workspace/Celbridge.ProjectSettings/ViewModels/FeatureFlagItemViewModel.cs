using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// The descriptive fields of one feature flag, resolved from the catalog and the project config.
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
    /// The value the flag takes when the project does not set it.
    /// </summary>
    public bool ApplicationValue { get; init; }

    /// <summary>
    /// The value the project's features table sets for the flag, or null when the project leaves it at the
    /// default.
    /// </summary>
    public bool? ProjectValue { get; init; }
}

/// <summary>
/// One feature flag in the Features section, with an on/off toggle. Switching the toggle to the flag's default
/// clears the project's entry, and switching it away writes the value, so a project file names only the flags
/// it changes. Changes are written to the .celbridge file and apply when the project is reloaded.
/// </summary>
public partial class FeatureFlagItemViewModel : ObservableObject
{
    private readonly FeatureFlagItemInfo _info;
    private readonly Action<string, bool?> _setProjectValue;

    private bool _recordsChanges;

    [ObservableProperty]
    private bool _isOn;

    /// <summary>
    /// True while the project file sets this flag, including an entry that matches the default.
    /// </summary>
    [ObservableProperty]
    private bool _hasProjectValue;

    public FeatureFlagItemViewModel(FeatureFlagItemInfo info, Action<string, bool?> setProjectValue)
    {
        _info = info;
        _setProjectValue = setProjectValue;

        IsOn = info.ProjectValue ?? info.ApplicationValue;
        HasProjectValue = info.ProjectValue is not null;
        _recordsChanges = true;
    }

    public string Title => _info.Title;

    public string Description => _info.Description;

    /// <summary>
    /// States the flag's default, shown as the toggle's tooltip.
    /// </summary>
    public string DefaultTooltip
    {
        get
        {
            if (_info.ApplicationValue)
            {
                return ProjectSettingsLabels.FeatureFlagDefaultOnTooltip;
            }

            return ProjectSettingsLabels.FeatureFlagDefaultOffTooltip;
        }
    }

    /// <summary>
    /// Returns the toggle to the flag's default without recording a change, for a reset that has already
    /// cleared the project's entry.
    /// </summary>
    public void ShowDefault()
    {
        _recordsChanges = false;
        IsOn = _info.ApplicationValue;
        HasProjectValue = false;
        _recordsChanges = true;
    }

    partial void OnIsOnChanged(bool value)
    {
        if (!_recordsChanges)
        {
            return;
        }

        // A value that matches the default is stored as no entry, so the project follows the default.
        if (value == _info.ApplicationValue)
        {
            HasProjectValue = false;
            _setProjectValue(_info.FlagName, null);
        }
        else
        {
            HasProjectValue = true;
            _setProjectValue(_info.FlagName, value);
        }
    }
}
