using System.Diagnostics;
using Celbridge.Automation;
using Celbridge.Explorer.Helpers;
using Celbridge.Explorer.Models;
using Celbridge.Logging;
using Celbridge.Platform;
using Celbridge.Projects;
using Celbridge.Resources.Services;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Services;

/// <summary>
/// The rows of the Explorer's resource tree, and the folder listings they come from. The model lists only the
/// folders the tree shows. In a folder with 50 visible subfolders or fewer, the model also checks each collapsed
/// subfolder for a child the tree shows. The answer decides the subfolder's expander. A subscription follows the
/// changes in the listed and checked folders.
/// </summary>
public sealed class ResourceTreeModel
{
    private sealed record ResourceLocation(IReadOnlyList<ResourceKey> FoldersAbove, bool IsHidden);

    // A folder's children, from the model's last listing of the folder.
    private sealed record FolderListing(IReadOnlyList<FolderChild> Children)
    {
        public FolderChild? Find(ResourceKey resource)
        {
            return Children.FirstOrDefault(folderChild => folderChild.Resource == resource);
        }
    }

    // Collects the rows in display order. A resource keeps its previous row when the row's appearance is unchanged.
    private sealed class RowBuilder
    {
        private readonly List<ResourceViewItem> _rows;
        private readonly Dictionary<ResourceKey, ResourceViewItem> _rowsByResource;
        private readonly Dictionary<ResourceKey, ResourceViewItem> _previousRowsByResource;

        public RowBuilder(
            List<ResourceViewItem> rows,
            Dictionary<ResourceKey, ResourceViewItem> rowsByResource,
            Dictionary<ResourceKey, ResourceViewItem> previousRowsByResource)
        {
            _rows = rows;
            _rowsByResource = rowsByResource;
            _previousRowsByResource = previousRowsByResource;
        }

        public void Add(
            ResourceKey resource,
            ResourceViewItemAppearance appearance,
            Func<ResourceKey, IconDefinition>? getFileIcon)
        {
            if (!_previousRowsByResource.TryGetValue(resource, out var row)
                || row.Appearance != appearance)
            {
                var fileIcon = getFileIcon?.Invoke(resource);
                row = new ResourceViewItem(resource, appearance, fileIcon);
            }

            _rows.Add(row);
            _rowsByResource[resource] = row;
        }
    }

    // The model checks the collapsed subfolders of a visible folder only when the folder has this many visible
    // subfolders or fewer. Above the limit, every collapsed subfolder row shows an expander.
    private const int LookAheadSubfolderLimit = 50;

    private readonly ILogger<ResourceTreeModel> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectService _projectService;
    private readonly IPlatformInfo _platformInfo;
    private readonly IIconService _iconService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IAutomationService _automationService;

    private readonly Dictionary<ResourceKey, FolderListing> _listings = new();

    // Maps each checked folder to true when the folder has a child the tree shows. The answers depend on Show Hidden
    // Files.
    private readonly Dictionary<ResourceKey, bool> _expanderChecks = new();
    private Dictionary<ResourceKey, ResourceViewItem> _rowsByResource = new();
    private IReadOnlyList<ResourceViewItem> _rows = [];

    private IMatcherSubscription? _subscription;
    private HashSet<ResourceKey> _followedFolders = new();

    public ResourceTreeModel(
        ILogger<ResourceTreeModel> logger,
        IWorkspaceWrapper workspaceWrapper,
        IProjectService projectService,
        IPlatformInfo platformInfo,
        IIconService iconService,
        IStringLocalizer stringLocalizer,
        IAutomationService automationService)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _projectService = projectService;
        _platformInfo = platformInfo;
        _iconService = iconService;
        _stringLocalizer = stringLocalizer;
        _automationService = automationService;
    }

    /// <summary>
    /// Raised when a change on disk alters the rows.
    /// </summary>
    public event Action? RowsChanged;

    /// <summary>
    /// The rows of the tree in display order, starting with the project folder.
    /// </summary>
    public IReadOnlyList<ResourceViewItem> Rows => _rows;

    /// <summary>
    /// The folders the model has listed.
    /// </summary>
    public IReadOnlyCollection<ResourceKey> ListedFolders => _listings.Keys;

    /// <summary>
    /// The folders the model has checked for a child the tree shows.
    /// </summary>
    public IReadOnlyCollection<ResourceKey> CheckedFolders => _expanderChecks.Keys;

    /// <summary>
    /// Returns true if the tree shows the resources it hides by default.
    /// </summary>
    public bool ShowHiddenFiles => WorkspaceSettings.ShowHiddenFiles;

    private IResourceService ResourceService => _workspaceWrapper.WorkspaceService.ResourceService;

    private IFolderStateService FolderStateService => _workspaceWrapper.WorkspaceService.ExplorerService.FolderStateService;

    private IBindableWorkspaceSettings WorkspaceSettings => _workspaceWrapper.WorkspaceService.BindableWorkspaceSettings;

    /// <summary>
    /// Lists each folder the tree shows, reusing the listings the model has, and checks the collapsed subfolders
    /// within the look-ahead limit. Then rebuilds the rows.
    /// </summary>
    public async Task RefreshAsync()
    {
        await UpdateAsync([]);
    }

    /// <summary>
    /// Expands or collapses a folder, then lists what the tree shows and rebuilds the rows.
    /// </summary>
    public async Task SetExpandedAsync(ResourceKey folder, bool isExpanded)
    {
        // UpdateAsync collapses an expanded folder when the parent's listing lacks the folder. The parent is listed
        // again first if its listing lacks the folder.
        if (isExpanded && !folder.IsEmpty)
        {
            await GetListingContainingAsync(folder.GetParent(), folder);
        }

        FolderStateService.SetExpanded(folder, isExpanded);

        await UpdateAsync([]);
    }

    /// <summary>
    /// Collapses every folder and rebuilds the rows.
    /// </summary>
    public async Task CollapseAllAsync()
    {
        var expandedFolders = FolderStateService.ExpandedFolders.ToList();
        foreach (var expandedFolder in expandedFolders)
        {
            FolderStateService.SetExpanded(expandedFolder, false);
        }

        await UpdateAsync([]);
    }

    /// <summary>
    /// Shows or hides the resources the tree hides by default, then lists what the tree shows and rebuilds the
    /// rows.
    /// </summary>
    public async Task SetShowHiddenFilesAsync(bool showHiddenFiles)
    {
        SetShowHiddenFiles(showHiddenFiles);

        await UpdateAsync([]);
    }

    /// <summary>
    /// Expands each folder above the resource, listing each one on the way down. Turns Show Hidden Files on when
    /// the tree hides the resource or a folder above it. Fails when the resource does not exist.
    /// </summary>
    public async Task<Result> RevealAsync(ResourceKey resource)
    {
        var locateResult = await LocateResourceAsync(resource);

        if (locateResult.IsSuccess)
        {
            var location = locateResult.Value;

            foreach (var folder in location.FoldersAbove)
            {
                FolderStateService.SetExpanded(folder, true);
            }

            if (location.IsHidden)
            {
                SetShowHiddenFiles(true);
            }
        }

        // LocateResourceAsync may list folders the tree does not show. Updating drops those listings.
        await UpdateAsync([]);

        if (locateResult.IsFailure)
        {
            return Result.Fail(locateResult);
        }

        return Result.Ok();
    }

    /// <summary>
    /// Stops following changes and drops every listing and check. The rows are left unchanged.
    /// </summary>
    public void Stop()
    {
        _subscription?.Dispose();
        _subscription = null;
        _followedFolders = new();

        _listings.Clear();
        _expanderChecks.Clear();
    }

    // Show Hidden Files decides which children count, so a change to the setting drops every check. The next update
    // makes each check again.
    private void SetShowHiddenFiles(bool showHiddenFiles)
    {
        if (WorkspaceSettings.ShowHiddenFiles == showHiddenFiles)
        {
            return;
        }

        WorkspaceSettings.ShowHiddenFiles = showHiddenFiles;
        _expanderChecks.Clear();
    }

    // Finds the folders above the resource by listing each one from the project folder down. Also reports whether
    // the tree hides the resource or any of those folders.
    private async Task<Result<ResourceLocation>> LocateResourceAsync(ResourceKey resource)
    {
        if (resource.Root != ResourceKey.DefaultRoot)
        {
            return Result.Fail($"The resource tree shows only the project's resources: '{resource}'");
        }

        var policy = ResourceService.Policy;
        var folders = new List<ResourceKey>();
        var isHidden = false;

        var segments = resource.Path.Split('/');
        var folder = ResourceKey.Empty;

        for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
        {
            var childKey = folder.Combine(segments[segmentIndex]);

            var listing = await GetListingContainingAsync(folder, childKey);
            if (listing is null)
            {
                return Result.Fail($"Failed to list folder: '{folder}'");
            }

            var folderChild = listing.Find(childKey);
            if (folderChild is null)
            {
                return Result.Fail($"Resource not found: '{resource}'");
            }

            if (ResourceTreeFilter.IsHidden(folderChild.Resource, folderChild.IsFolder, policy))
            {
                isHidden = true;
            }

            var isLastSegment = segmentIndex == segments.Length - 1;
            if (isLastSegment)
            {
                break;
            }

            if (!folderChild.IsFolder)
            {
                return Result.Fail($"Resource not found: '{resource}'");
            }

            folders.Add(childKey);
            folder = childKey;
        }

        return new ResourceLocation(folders, isHidden);
    }

    // Lists and checks the stale folders again. The model then keeps a listing of exactly the folders the tree
    // shows. Those are the project folder and each expanded folder inside a visible folder. The model keeps a check
    // of each collapsed subfolder of a visible folder, within the look-ahead limit.
    private async Task UpdateAsync(IReadOnlyCollection<ResourceKey> staleFolders)
    {
        var stopwatch = Stopwatch.StartNew();

        foreach (var staleFolder in staleFolders)
        {
            _listings.Remove(staleFolder);
            _expanderChecks.Remove(staleFolder);
        }

        var neededFolders = new HashSet<ResourceKey>();
        var checkedFolders = new HashSet<ResourceKey>();
        var visibleFolders = new Queue<ResourceKey>();
        visibleFolders.Enqueue(ResourceKey.Empty);

        while (visibleFolders.TryDequeue(out var visibleFolder))
        {
            neededFolders.Add(visibleFolder);

            var listing = await GetListingAsync(visibleFolder);
            if (listing is null)
            {
                continue;
            }

            var visibleSubfolders = listing.Children
                .Where(folderChild => folderChild.IsFolder && IsVisible(folderChild))
                .ToList();
            var checksSubfolders = visibleSubfolders.Count <= LookAheadSubfolderLimit;

            foreach (var subfolder in visibleSubfolders)
            {
                if (FolderStateService.IsExpanded(subfolder.Resource))
                {
                    visibleFolders.Enqueue(subfolder.Resource);
                }
                else if (checksSubfolders)
                {
                    checkedFolders.Add(subfolder.Resource);
                    await CheckForVisibleChildAsync(subfolder.Resource);
                }
            }
        }

        var unneededListings = _listings.Keys
            .Where(folder => !neededFolders.Contains(folder))
            .ToList();
        foreach (var unneededListing in unneededListings)
        {
            _listings.Remove(unneededListing);
        }

        var unneededChecks = _expanderChecks.Keys
            .Where(folder => !checkedFolders.Contains(folder))
            .ToList();
        foreach (var unneededCheck in unneededChecks)
        {
            _expanderChecks.Remove(unneededCheck);
        }

        RemoveMissingExpandedFolders(neededFolders);
        UpdateSubscription();
        BuildRows();

        var elapsed = (long)stopwatch.Elapsed.TotalMilliseconds;
        _automationService.LogEvent(new ExplorerTreeBuiltEvent(_rows.Count, elapsed));
    }

    private async Task<FolderListing?> GetListingAsync(ResourceKey folder)
    {
        if (_listings.TryGetValue(folder, out var heldListing))
        {
            return heldListing;
        }

        var enumerateResult = await ResourceService.FileSystem.EnumerateFolderAsync(folder);
        if (enumerateResult.IsFailure)
        {
            if (folder.IsEmpty)
            {
                _logger.LogWarning(enumerateResult, "Failed to list the project folder");
            }

            return null;
        }

        var listing = new FolderListing(enumerateResult.Value);
        _listings[folder] = listing;

        return listing;
    }

    // Records whether the folder has a child the tree shows, unless the model has the answer already. A folder the
    // check fails on gets no answer, so its row shows an expander.
    private async Task CheckForVisibleChildAsync(ResourceKey folder)
    {
        if (_expanderChecks.ContainsKey(folder))
        {
            return;
        }

        var checkResult = await ResourceService.FileSystem.HasChildAsync(folder, IsVisible);
        if (checkResult.IsFailure)
        {
            return;
        }

        _expanderChecks[folder] = checkResult.Value;
    }

    // Lists the folder again when its held listing lacks the resource. A command that changes the disk can name the
    // resource before the change reaches the model, so a held listing can predate the resource.
    private async Task<FolderListing?> GetListingContainingAsync(ResourceKey folder, ResourceKey resource)
    {
        var listing = await GetListingAsync(folder);
        if (listing is not null
            && listing.Find(resource) is not null)
        {
            return listing;
        }

        _listings.Remove(folder);

        return await GetListingAsync(folder);
    }

    // An expanded folder leaves the saved set when the parent folder's listing lacks the folder. It also leaves when
    // the folder is visible and listing the folder fails.
    private void RemoveMissingExpandedFolders(IReadOnlySet<ResourceKey> neededFolders)
    {
        var missingFolders = new List<ResourceKey>();

        foreach (var expandedFolderPath in FolderStateService.ExpandedFolders)
        {
            var expandedFolder = new ResourceKey(expandedFolderPath);

            if (neededFolders.Contains(expandedFolder)
                && !_listings.ContainsKey(expandedFolder))
            {
                missingFolders.Add(expandedFolder);
                continue;
            }

            var parentFolder = expandedFolder.GetParent();
            if (!_listings.TryGetValue(parentFolder, out var parentListing))
            {
                continue;
            }

            var folderChild = parentListing.Find(expandedFolder);
            if (folderChild is null
                || !folderChild.IsFolder)
            {
                missingFolders.Add(expandedFolder);
            }
        }

        foreach (var missingFolder in missingFolders)
        {
            FolderStateService.SetExpanded(missingFolder, false);
        }
    }

    // Subscribes again whenever the listed or checked folders change, before disposing the old subscription so no
    // change is missed between the two.
    private void UpdateSubscription()
    {
        var followedFolders = GetFollowedFolders();
        if (_subscription is not null
            && _followedFolders.SetEquals(followedFolders))
        {
            return;
        }

        var matcher = new FollowedFoldersMatcher(followedFolders);
        var handlers = new ResourceChangeHandlers(OnChanges, OnResync);

        var subscribeResult = ResourceService.Monitor.SubscribeToMatcher(ResourceKey.DefaultRoot, matcher, handlers);
        if (subscribeResult.IsFailure)
        {
            _logger.LogWarning(subscribeResult, "Failed to subscribe to the changes in the resource tree's folders");
            return;
        }

        var previousSubscription = _subscription;
        _subscription = subscribeResult.Value;
        _followedFolders = followedFolders;
        previousSubscription?.Dispose();
    }

    private HashSet<ResourceKey> GetFollowedFolders()
    {
        var followedFolders = _listings.Keys.ToHashSet();
        followedFolders.UnionWith(_expanderChecks.Keys);

        return followedFolders;
    }

    private void OnChanges(ResourceChangeBatch batch)
    {
        var staleFolders = FindStaleFolders(batch.Changes);
        if (staleFolders.Count == 0)
        {
            return;
        }

        _ = UpdateAfterChangesAsync(staleFolders);
    }

    private void OnResync(ResourceResync resync)
    {
        var staleFolders = GetFollowedFolders();

        _ = UpdateAfterChangesAsync(staleFolders);
    }

    private async Task UpdateAfterChangesAsync(IReadOnlyCollection<ResourceKey> staleFolders)
    {
        try
        {
            var previousRows = _rows;

            await UpdateAsync(staleFolders);

            if (!_rows.SequenceEqual(previousRows))
            {
                RowsChanged?.Invoke();
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to update the resource tree after a change");
        }
    }

    // A change makes a followed folder stale when the folder contains the changed item, is the changed item, or is
    // inside the changed item. Letter case is ignored, because a delete on macOS reports the name in the case the
    // caller used.
    private List<ResourceKey> FindStaleFolders(IReadOnlyList<ResourceChange> changes)
    {
        var staleFolders = new HashSet<ResourceKey>();
        var followedFolders = GetFollowedFolders();

        foreach (var change in changes)
        {
            var changedPath = change.Resource.Path;
            var parentPath = change.Resource.GetParent().Path;

            foreach (var followedFolder in followedFolders)
            {
                var followedPath = followedFolder.Path;

                if (string.Equals(followedPath, parentPath, StringComparison.OrdinalIgnoreCase)
                    || ContainsPath(changedPath, followedPath))
                {
                    staleFolders.Add(followedFolder);
                }
            }
        }

        return staleFolders.ToList();
    }

    // Returns true if path equals containerPath or lies inside containerPath, ignoring letter case.
    private static bool ContainsPath(string containerPath, string path)
    {
        if (string.Equals(containerPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (containerPath.Length == 0)
        {
            return true;
        }

        return path.Length > containerPath.Length
            && path[containerPath.Length] == '/'
            && path.StartsWith(containerPath, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsVisible(FolderChild folderChild)
    {
        if (ShowHiddenFiles)
        {
            return true;
        }

        return !ResourceTreeFilter.IsHidden(folderChild.Resource, folderChild.IsFolder, ResourceService.Policy);
    }

    // Returns true if the folder has a visible child. A folder the model has neither listed nor checked counts as
    // having one, so its row shows an expander.
    private bool HasVisibleChildren(ResourceKey folder)
    {
        if (_listings.TryGetValue(folder, out var listing))
        {
            return listing.Children.Any(IsVisible);
        }

        if (_expanderChecks.TryGetValue(folder, out var hasVisibleChild))
        {
            return hasVisibleChild;
        }

        return true;
    }

    private void BuildRows()
    {
        var previousRowsByResource = _rowsByResource;
        var rowsByResource = new Dictionary<ResourceKey, ResourceViewItem>();
        var rows = new List<ResourceViewItem>();

        var projectFolderAppearance = new ResourceViewItemAppearance(
            Name: GetProjectFolderName(),
            IndentLevel: 0,
            IsFolder: true,
            HasChildren: HasVisibleChildren(ResourceKey.Empty),
            IsExpanded: true,
            IsProjectFolder: true,
            IsHidden: false,
            WritableState: WritableState.Writable,
            ReadOnlyMessage: string.Empty,
            ProjectFolderTooltip: GetProjectFolderTooltip());

        var builder = new RowBuilder(rows, rowsByResource, previousRowsByResource);
        builder.Add(ResourceKey.Empty, projectFolderAppearance, null);

        // The project folder's children sit at indent level 0, since the project folder row has a negative margin.
        AddChildRows(builder, ResourceKey.Empty, indentLevel: 0);

        _rowsByResource = rowsByResource;
        _rows = rows;
    }

    private void AddChildRows(RowBuilder builder, ResourceKey folder, int indentLevel)
    {
        if (!_listings.TryGetValue(folder, out var listing))
        {
            return;
        }

        var policy = ResourceService.Policy;
        var rootHandlers = ResourceService.RootHandlers;
        var showHiddenFiles = ShowHiddenFiles;

        foreach (var folderChild in listing.Children)
        {
            var isHidden = ResourceTreeFilter.IsHidden(folderChild.Resource, folderChild.IsFolder, policy);
            if (isHidden
                && !showHiddenFiles)
            {
                continue;
            }

            var writableState = WritableStatePriority.Compute(folderChild.Resource, folderChild.Attributes, rootHandlers);
            var readOnlyMessage = ReadOnlyMessageHelper.GetReadOnlyMessage(writableState, _stringLocalizer) ?? string.Empty;

            if (folderChild.IsFolder)
            {
                var isExpanded = FolderStateService.IsExpanded(folderChild.Resource);
                var hasChildren = HasVisibleChildren(folderChild.Resource);

                var appearance = new ResourceViewItemAppearance(
                    Name: folderChild.Resource.ResourceName,
                    IndentLevel: indentLevel,
                    IsFolder: true,
                    HasChildren: hasChildren,
                    IsExpanded: isExpanded,
                    IsProjectFolder: false,
                    IsHidden: isHidden,
                    WritableState: writableState,
                    ReadOnlyMessage: readOnlyMessage,
                    ProjectFolderTooltip: string.Empty);
                builder.Add(folderChild.Resource, appearance, null);

                if (isExpanded
                    && hasChildren)
                {
                    AddChildRows(builder, folderChild.Resource, indentLevel + 1);
                }
            }
            else
            {
                var appearance = new ResourceViewItemAppearance(
                    Name: folderChild.Resource.ResourceName,
                    IndentLevel: indentLevel,
                    IsFolder: false,
                    HasChildren: false,
                    IsExpanded: false,
                    IsProjectFolder: false,
                    IsHidden: isHidden,
                    WritableState: writableState,
                    ReadOnlyMessage: readOnlyMessage,
                    ProjectFolderTooltip: string.Empty);
                builder.Add(folderChild.Resource, appearance, GetFileIcon);
            }
        }
    }

    private IconDefinition GetFileIcon(ResourceKey file)
    {
        var getIconResult = _iconService.GetFileIconForFileName(file.ResourceName);
        if (getIconResult.IsSuccess)
        {
            return getIconResult.Value;
        }

        return _iconService.DefaultFileIcon;
    }

    private string GetProjectFolderName()
    {
        var projectFolderPath = _projectService.CurrentProject?.ProjectFolderPath ?? string.Empty;

        return Path.GetFileName(projectFolderPath);
    }

    private string GetProjectFolderTooltip()
    {
        string fileManagerName = _stringLocalizer.GetString(_platformInfo.FileManagerNameStringKey);

        return _stringLocalizer.GetString("ResourceTree_DoubleClickToOpen", fileManagerName);
    }
}
