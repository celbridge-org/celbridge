using Celbridge.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// The descriptive fields of one page row, resolved from a discovered pages.toml manifest by the Pages
/// section view model.
/// </summary>
public sealed record PageItemInfo
{
    /// <summary>
    /// The served path declared in the manifest's [publish].path, or empty when the manifest could not be
    /// parsed.
    /// </summary>
    public string PublishPath { get; init; } = string.Empty;

    /// <summary>
    /// Resource key of the pages.toml manifest, always openable because discovery reads it from the
    /// project resource tree. The page folder is its parent.
    /// </summary>
    public ResourceKey ManifestResource { get; init; }

    /// <summary>
    /// True when the manifest's published path could not be read, so the page shows a warning.
    /// </summary>
    public bool HasManifestIssue { get; init; }
}

/// <summary>
/// One discovered page on the Pages section of Project Settings: its published path, its location, and a
/// link to its manifest. Pages are read-only here; publishing is handled by the page tools.
/// </summary>
public partial class PageItemViewModel : ObservableObject
{
    private readonly PageItemInfo _info;
    private readonly Action<ResourceKey> _openManifest;
    private readonly Action<ResourceKey> _revealManifest;

    // The page folder is the manifest's parent, cached because it backs both the title fallback and the
    // location text.
    private readonly ResourceKey _folderResource;

    public PageItemViewModel(
        PageItemInfo info,
        Action<ResourceKey> openManifest,
        Action<ResourceKey> revealManifest)
    {
        _info = info;
        _openManifest = openManifest;
        _revealManifest = revealManifest;
        _folderResource = info.ManifestResource.GetParent();
    }

    /// <summary>
    /// The card header: the published path, falling back to the folder location when the manifest declares
    /// no valid path.
    /// </summary>
    public string Title => HasPublishPath ? _info.PublishPath : _folderResource.Path;

    // Backs the title fallback: with no published path the folder path stands in as the header.
    private bool HasPublishPath => !string.IsNullOrEmpty(_info.PublishPath);

    public string LocationText => _folderResource.Path;

    public bool HasIssue => _info.HasManifestIssue;

    public string IssueText => ProjectSettingsLabels.PageManifestIssue;

    /// <summary>
    /// File name of the page manifest, shown as the text of the link that opens it.
    /// </summary>
    public string ManifestFileName => _info.ManifestResource.ResourceName;

    public string LocationLabel => ProjectSettingsLabels.PageLocationLabel;
    public string ManifestLabel => ProjectSettingsLabels.ManifestLabel;
    public string ManifestIssueTitle => ProjectSettingsLabels.PageManifestIssueTitle;
    public string OpenManifestTooltip => ProjectSettingsLabels.OpenManifestTooltip;
    public string RevealManifestTooltip => ProjectSettingsLabels.RevealManifestTooltip;

    [RelayCommand]
    private void OpenManifest()
    {
        _openManifest(_info.ManifestResource);
    }

    [RelayCommand]
    private void RevealManifest()
    {
        _revealManifest(_info.ManifestResource);
    }
}
