using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// The descriptive fields of one package row, resolved from the package and its manifest by the Packages
/// section view model.
/// </summary>
public sealed record PackageItemInfo
{
    /// <summary>
    /// Name identifying the package, written to the disabled-packages list.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The package name as shown beneath the display name, carrying the built-in suffix for a bundled
    /// package.
    /// </summary>
    public string NameLabel { get; init; } = string.Empty;

    /// <summary>
    /// Localized title of the package.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Absolute path of the package manifest.
    /// </summary>
    public string ManifestPath { get; init; } = string.Empty;

    /// <summary>
    /// Whether the manifest can be opened in the workspace. False for a bundled package, whose manifest
    /// lives in the application folder.
    /// </summary>
    public bool CanOpenManifest { get; init; }
}

/// <summary>
/// One discovered package on the Packages section, with an enable/disable toggle and the contributions it
/// ships nested beneath it. Disabling a package writes it to [celbridge].disabled-packages so it
/// contributes nothing on reload.
/// </summary>
public partial class PackageItemViewModel : ObservableObject
{
    private readonly PackageItemInfo _info;
    private readonly Action<string, bool> _setDisabled;
    private readonly Action<string> _openManifest;

    private bool _initialized;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderOpacity))]
    private bool _isEnabled;

    public PackageItemViewModel(
        PackageItemInfo info,
        bool isEnabled,
        Action<string, bool> setDisabled,
        Action<string> openManifest)
    {
        _info = info;
        _setDisabled = setDisabled;
        _openManifest = openManifest;

        IsEnabled = isEnabled;
        _initialized = true;
    }

    public string Name => _info.Name;
    public string NameLabel => _info.NameLabel;
    public string DisplayName => _info.DisplayName;

    public bool CanOpenManifest => _info.CanOpenManifest;

    /// <summary>
    /// File name of the package manifest, shown as the text of the link that opens it.
    /// </summary>
    public string ManifestFileName => System.IO.Path.GetFileName(_info.ManifestPath);

    /// <summary>
    /// Dims the header of a disabled package.
    /// </summary>
    public double HeaderOpacity => IsEnabled ? 1.0 : 0.5;

    public string ToggleTooltip => ProjectSettingsLabels.PackageToggleTooltip;

    public string EnabledLabel => ProjectSettingsLabels.PackageEnabledLabel;

    public string ManifestLabel => ProjectSettingsLabels.ManifestLabel;

    public string OpenManifestTooltip => ProjectSettingsLabels.OpenManifestTooltip;

    /// <summary>
    /// The contributions this package ships, shown nested under the package row.
    /// </summary>
    public ObservableCollection<ContributionItemViewModel> Contributions { get; } = new();

    /// <summary>
    /// Whether the package ships any contributions to show beneath it.
    /// </summary>
    public bool HasContributions => Contributions.Count > 0;

    [RelayCommand]
    private void OpenManifest()
    {
        _openManifest(_info.ManifestPath);
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_initialized)
        {
            _setDisabled(Name, !value);
        }
    }
}
