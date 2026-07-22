using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// The descriptive fields of one contribution row, resolved from the contribution and its manifest by
/// the Packages section view model.
/// </summary>
public sealed record ContributionItemInfo
{
    /// <summary>
    /// Name of the package that ships the contribution.
    /// </summary>
    public string PackageName { get; init; } = string.Empty;

    /// <summary>
    /// Identifier of the contribution within its package.
    /// </summary>
    public string ContributionId { get; init; } = string.Empty;

    /// <summary>
    /// Localized name of the contribution.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Localized description of the contribution, or empty when the manifest declares none.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Whether the contribution is a utility rather than a document editor.
    /// </summary>
    public bool IsUtility { get; init; }

    /// <summary>
    /// The glyph name shown beside the contribution, or empty when it declares no icon.
    /// </summary>
    public string IconGlyph { get; init; } = string.Empty;

    /// <summary>
    /// Absolute path of the editor manifest the contribution was parsed from.
    /// </summary>
    public string ManifestPath { get; init; } = string.Empty;

    /// <summary>
    /// Whether the manifest can be opened in the workspace. False for a bundled package, whose manifest
    /// lives in the application folder.
    /// </summary>
    public bool CanOpenManifest { get; init; }

    /// <summary>
    /// Whether the contribution is opt-in. Governs whether the enable toggle writes an enabled marker
    /// (optional) or a disabled marker (recommended).
    /// </summary>
    public bool IsOptional { get; init; }

    /// <summary>
    /// Whether the project may turn this contribution on or off. False for a required contribution,
    /// which is always active and shows no toggle.
    /// </summary>
    public bool CanToggle { get; init; }

    /// <summary>
    /// The editor id ("{package}.{contribution}") this contribution registers under.
    /// </summary>
    public string EditorId { get; init; } = string.Empty;

    /// <summary>
    /// The file types the contribution claims.
    /// </summary>
    public IReadOnlyList<FileTypeInfo> FileTypes { get; init; } = [];
}

/// <summary>
/// One discovered contribution row under its package on the Packages section of Project Settings, carrying
/// the contribution identity, an enable toggle, and the descriptor form fields.
/// </summary>
public partial class ContributionItemViewModel : ObservableObject
{
    private readonly ContributionItemInfo _info;
    private readonly Action<ContributionItemViewModel, bool> _setEnabled;
    private readonly Action<string> _openManifest;

    private bool _initialized;

    [ObservableProperty]
    private bool _isEnabled;

    public ContributionItemViewModel(
        ContributionItemInfo info,
        Action<ContributionItemViewModel, bool> setEnabled,
        Action<string> openManifest)
    {
        _info = info;
        _setEnabled = setEnabled;
        _openManifest = openManifest;
    }

    public string PackageName => _info.PackageName;
    public string ContributionId => _info.ContributionId;
    public string DisplayName => _info.DisplayName;
    public bool IsUtility => _info.IsUtility;
    public string IconGlyph => _info.IconGlyph;
    public bool IsOptional => _info.IsOptional;
    public bool CanToggle => _info.CanToggle;
    public string EditorId => _info.EditorId;
    public IReadOnlyList<FileTypeInfo> FileTypes => _info.FileTypes;

    /// <summary>
    /// The contribution's description as the row tooltip, or null when it declares none.
    /// </summary>
    public string? Tooltip => string.IsNullOrEmpty(_info.Description) ? null : _info.Description;

    /// <summary>
    /// Whether the contribution has a configured icon to show.
    /// </summary>
    public bool HasIcon => !string.IsNullOrEmpty(IconGlyph);

    /// <summary>
    /// The extensions this contribution claims, as a comma separated list.
    /// </summary>
    public string FileExtensionsText => string.Join(", ", FileTypes.Select(fileType => fileType.Extension));

    /// <summary>
    /// Whether the contribution claims any extensions to list.
    /// </summary>
    public bool HasFileExtensions => FileTypes.Count > 0;

    public string FileExtensionsLabel => ProjectSettingsLabels.FileExtensionsLabel;

    /// <summary>
    /// A short caption naming the contribution's type: a document editor or a utility.
    /// </summary>
    public string TypeLabel => IsUtility ? ProjectSettingsLabels.UtilityTypeLabel : ProjectSettingsLabels.DocumentTypeLabel;

    public bool CanOpenManifest => _info.CanOpenManifest;

    /// <summary>
    /// File name of the editor manifest, shown as the text of the link that opens it.
    /// </summary>
    public string ManifestFileName => System.IO.Path.GetFileName(_info.ManifestPath);

    public string EnabledLabel => ProjectSettingsLabels.ContributionEnabledLabel;

    public string ManifestLabel => ProjectSettingsLabels.ManifestLabel;

    public string ToggleTooltip => ProjectSettingsLabels.ContributionToggleTooltip;

    public string OpenManifestTooltip => ProjectSettingsLabels.OpenManifestTooltip;

    public ObservableCollection<ConfigFieldViewModel> ConfigFields { get; } = new();

    /// <summary>
    /// Whether the descriptor form has any fields to show.
    /// </summary>
    public bool HasConfigFields => ConfigFields.Count > 0;

    /// <summary>
    /// Sets the initial enabled value without triggering a write, then enables commits.
    /// </summary>
    public void InitializeState(bool isEnabled)
    {
        IsEnabled = isEnabled;
        _initialized = true;
    }

    [RelayCommand]
    private void OpenManifest()
    {
        _openManifest(_info.ManifestPath);
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_initialized)
        {
            _setEnabled(this, value);
        }
    }
}
