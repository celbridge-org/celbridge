using System.Collections.ObjectModel;
using Celbridge.Core;
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
    /// Resource key of the package manifest, or null when the manifest cannot be opened in the workspace
    /// (a bundled package's manifest lives in the application folder, outside every registered root).
    /// </summary>
    public ResourceKey? ManifestResource { get; init; }

    /// <summary>
    /// Installed package version, or null when the package records no parseable version.
    /// </summary>
    public int? Version { get; init; }
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
    private readonly Action<ResourceKey> _openManifest;
    private readonly Action<ResourceKey> _revealManifest;

    private bool _initialized;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderOpacity))]
    private bool _isEnabled;

    public PackageItemViewModel(
        PackageItemInfo info,
        bool isEnabled,
        Action<string, bool> setDisabled,
        Action<ResourceKey> openManifest,
        Action<ResourceKey> revealManifest)
    {
        _info = info;
        _setDisabled = setDisabled;
        _openManifest = openManifest;
        _revealManifest = revealManifest;

        IsEnabled = isEnabled;
        _initialized = true;
    }

    public string Name => _info.Name;
    public string NameLabel => _info.NameLabel;
    public string DisplayName => _info.DisplayName;

    /// <summary>
    /// Whether the package records a version to show beside its name.
    /// </summary>
    public bool HasVersion => _info.Version is not null;

    /// <summary>
    /// The version shown beside the package name (e.g. "v3"), or empty when none is recorded.
    /// </summary>
    public string VersionText
    {
        get
        {
            if (_info.Version is int version)
            {
                return ProjectSettingsLabels.PackageVersion(version);
            }

            return string.Empty;
        }
    }

    public bool CanOpenManifest => _info.ManifestResource is not null;

    /// <summary>
    /// File name of the package manifest, shown as the text of the link that opens it.
    /// </summary>
    public string ManifestFileName => _info.ManifestResource?.ResourceName ?? string.Empty;

    /// <summary>
    /// Dims the header of a disabled package.
    /// </summary>
    public double HeaderOpacity => IsEnabled ? 1.0 : 0.5;

    public string ToggleTooltip => ProjectSettingsLabels.PackageToggleTooltip;

    public string EnabledLabel => ProjectSettingsLabels.PackageEnabledLabel;

    public string ManifestLabel => ProjectSettingsLabels.ManifestLabel;

    public string OpenManifestTooltip => ProjectSettingsLabels.OpenManifestTooltip;

    public string RevealManifestTooltip => ProjectSettingsLabels.RevealManifestTooltip;

    /// <summary>
    /// The contributions this package ships, shown nested under the package row.
    /// </summary>
    public ObservableCollection<ContributionItemViewModel> Contributions { get; } = new();

    /// <summary>
    /// Whether the package ships any contributions to show beneath it.
    /// </summary>
    public bool HasContributions => Contributions.Count > 0;

    /// <summary>
    /// Whether any contribution in this package had configuration dropped. Surfaced on the package
    /// header because the contributions sit inside the expander, which is collapsed by default.
    /// </summary>
    public bool HasIssues => Contributions.Any(contribution => contribution.HasIssues);

    /// <summary>
    /// Names the contributions with dropped configuration, as the tooltip of the header warning icon.
    /// </summary>
    public string IssuesTooltip => ProjectSettingsLabels.PackageIssues(
        Contributions.Where(contribution => contribution.HasIssues).Select(contribution => contribution.DisplayName).ToArray());

    [RelayCommand]
    private void OpenManifest()
    {
        if (_info.ManifestResource is { } manifestResource)
        {
            _openManifest(manifestResource);
        }
    }

    [RelayCommand]
    private void RevealManifest()
    {
        if (_info.ManifestResource is { } manifestResource)
        {
            _revealManifest(manifestResource);
        }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_initialized)
        {
            _setDisabled(Name, !value);
        }
    }
}
