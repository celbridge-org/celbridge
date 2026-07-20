using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// One discovered package on the Packages section, with an enable/disable toggle and the contributions it
/// ships nested beneath it. Disabling a package writes it to [celbridge].disabled-packages so it
/// contributes nothing on reload.
/// </summary>
public partial class PackageItemViewModel : ObservableObject
{
    private readonly Action<string, bool> _setDisabled;

    private bool _initialized;

    [ObservableProperty]
    private bool _isEnabled;

    public PackageItemViewModel(string name, string displayName, bool isEnabled, Action<string, bool> setDisabled)
    {
        Name = name;
        DisplayName = displayName;
        _setDisabled = setDisabled;

        IsEnabled = isEnabled;
        _initialized = true;
    }

    public string Name { get; }
    public string DisplayName { get; }

    public string ToggleTooltip => ProjectSettingsLabels.PackageToggleTooltip;

    /// <summary>
    /// The contributions this package ships, shown nested under the package row.
    /// </summary>
    public ObservableCollection<ContributionItemViewModel> Contributions { get; } = new();

    /// <summary>
    /// Whether the package ships any contributions to show beneath it.
    /// </summary>
    public bool HasContributions => Contributions.Count > 0;

    partial void OnIsEnabledChanged(bool value)
    {
        if (_initialized)
        {
            _setDisabled(Name, !value);
        }
    }
}
