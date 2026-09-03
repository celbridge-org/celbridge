using System.Collections.ObjectModel;
using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.Resources;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// Drives the Pages section: every pages.toml manifest discovered in the project tree, listed with its
/// published path and a link to its manifest. Pages are read-only here; publishing is handled by the page
/// tools.
/// </summary>
public class PagesSectionViewModel : ProjectSettingsSectionViewModel
{
    public ObservableCollection<PageItemViewModel> Pages { get; } = new();

    public PagesSectionViewModel(ProjectSettingsContext context)
        : base(context)
    {
    }

    public override string EmptyText => ProjectSettingsLabels.PagesEmpty;

    public override void Load()
    {
        Pages.Clear();

        var registry = WorkspaceService?.ResourceService.Registry;
        if (registry is null)
        {
            SetNotLoadedContentState();
            return;
        }

        // A page is a folder with a pages.toml manifest at its root, so every such manifest in the tree is
        // one page. The registry entry carries the manifest's resource key, which is always openable.
        var fileResources = registry.GetAllFileResources();
        foreach (var manifestEntry in fileResources)
        {
            var fileName = System.IO.Path.GetFileName(manifestEntry.Path);
            if (!string.Equals(fileName, PageConstants.ManifestFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var pageItem = BuildPage(manifestEntry);
            Pages.Add(pageItem);
        }

        SetLoadedContentState(Pages.Count);
    }

    private PageItemViewModel BuildPage(FileResourceEntry manifestEntry)
    {
        var parseResult = PageManifestParser.ParsePublishPathFromFile(manifestEntry.Path);

        var publishPath = string.Empty;
        if (parseResult.IsSuccess)
        {
            publishPath = parseResult.Value;
        }

        var info = new PageItemInfo
        {
            PublishPath = publishPath,
            ManifestResource = manifestEntry.Resource,
            HasManifestIssue = parseResult.IsFailure,
        };

        return new PageItemViewModel(info, OpenManifest, RevealManifest);
    }

}
