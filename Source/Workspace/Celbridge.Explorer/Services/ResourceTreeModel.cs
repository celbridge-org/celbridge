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
/// folders the tree shows, and follows the changes in those folders through a subscription.
/// </summary>
public sealed class ResourceTreeModel
{
    private sealed record PathToResource(IReadOnlyList<ResourceKey> Folders, bool IsHidden);

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

    // A visible folder with this many visible subfolders or fewer also lists each of them, so their expanders are
    // accurate. Above the limit, every subfolder row shows an expander.
    private const int LookAheadSubfolderLimit = 50;

    private readonly ILogger<ResourceTreeModel> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectService _projectService;
    private readonly IPlatformInfo _platformInfo;
    private readonly IIconService _iconService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IAutomationService _automationService;

    private readonly Dictionary<ResourceKey, IReadOnlyList<FolderChild>> _listings = new();
    private Dictionary<ResourceKey, ResourceViewItem> _rowsByResource = new();
    private IReadOnlyList<ResourceViewItem> _rows = [];

    private IMatcherSubscription? _subscription;
    private HashSet<ResourceKey> _subscribedFolders = new();

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
    /// Returns true if the tree shows the resources it hides by default.
    /// </summary>
    public bool ShowHiddenFiles => WorkspaceSettings.ShowHiddenFiles;

    private IResourceService ResourceService => _workspaceWrapper.WorkspaceService.ResourceService;

    private IFolderStateService FolderStateService => _workspaceWrapper.WorkspaceService.ExplorerService.FolderStateService;

    private IBindableWorkspaceSettings WorkspaceSettings => _workspaceWrapper.WorkspaceService.BindableWorkspaceSettings;

    /// <summary>
    /// Lists each folder the tree shows that the model has not listed, and rebuilds the rows.
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
        WorkspaceSettings.ShowHiddenFiles = showHiddenFiles;

        await UpdateAsync([]);
    }

    /// <summary>
    /// Expands each folder above the resource, listing each one on the way down. Turns Show Hidden Files on when
    /// the tree hides the resource or a folder above it. Fails when the resource does not exist.
    /// </summary>
    public async Task<Result> RevealAsync(ResourceKey resource)
    {
        var findResult = await FindPathToAsync(resource);

        if (findResult.IsSuccess)
        {
            var path = findResult.Value;

            foreach (var folder in path.Folders)
            {
                FolderStateService.SetExpanded(folder, true);
            }

            if (path.IsHidden)
            {
                WorkspaceSettings.ShowHiddenFiles = true;
            }
        }

        // FindPathToAsync may list folders the tree does not show. Updating drops those listings.
        await UpdateAsync([]);

        if (findResult.IsFailure)
        {
            return Result.Fail(findResult);
        }

        return Result.Ok();
    }

    /// <summary>
    /// Stops following changes and drops every listing. The rows are left unchanged.
    /// </summary>
    public void Stop()
    {
        _subscription?.Dispose();
        _subscription = null;
        _subscribedFolders = new();

        _listings.Clear();
    }

    // Finds the folders above the resource by listing each one from the project folder down. Also reports whether
    // the tree hides the resource or any of those folders.
    private async Task<Result<PathToResource>> FindPathToAsync(ResourceKey resource)
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
            var listing = await GetListingAsync(folder);
            if (listing is null)
            {
                return Result.Fail($"Failed to list folder: '{folder}'");
            }

            var itemKey = folder.Combine(segments[segmentIndex]);
            var item = listing.FirstOrDefault(folderChild => folderChild.Resource == itemKey);
            if (item is null)
            {
                return Result.Fail($"Resource not found: '{resource}'");
            }

            if (ResourceTreeFilter.IsHidden(item.Resource, item.IsFolder, policy))
            {
                isHidden = true;
            }

            var isLastSegment = segmentIndex == segments.Length - 1;
            if (isLastSegment)
            {
                break;
            }

            if (!item.IsFolder)
            {
                return Result.Fail($"Resource not found: '{resource}'");
            }

            folders.Add(itemKey);
            folder = itemKey;
        }

        return new PathToResource(folders, isHidden);
    }

    // Re-lists the stale folders, then keeps a listing of exactly the folders the tree needs. Those are the project
    // folder and each expanded folder inside a visible folder. They also include the subfolders of a visible folder,
    // within the look-ahead limit.
    private async Task UpdateAsync(IReadOnlyCollection<ResourceKey> staleFolders)
    {
        var stopwatch = Stopwatch.StartNew();

        foreach (var staleFolder in staleFolders)
        {
            _listings.Remove(staleFolder);
        }

        var neededFolders = new HashSet<ResourceKey>();
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

            var visibleSubfolders = listing
                .Where(item => item.IsFolder && IsVisible(item))
                .ToList();
            var listsSubfolders = visibleSubfolders.Count <= LookAheadSubfolderLimit;

            foreach (var subfolder in visibleSubfolders)
            {
                if (FolderStateService.IsExpanded(subfolder.Resource))
                {
                    visibleFolders.Enqueue(subfolder.Resource);
                }
                else if (listsSubfolders)
                {
                    neededFolders.Add(subfolder.Resource);
                    await GetListingAsync(subfolder.Resource);
                }
            }
        }

        var unneededFolders = _listings.Keys
            .Where(folder => !neededFolders.Contains(folder))
            .ToList();
        foreach (var unneededFolder in unneededFolders)
        {
            _listings.Remove(unneededFolder);
        }

        RemoveMissingExpandedFolders(neededFolders);
        UpdateSubscription();
        BuildRows();

        var elapsed = (long)stopwatch.Elapsed.TotalMilliseconds;
        _automationService.LogEvent(new ExplorerTreeBuiltEvent(_rows.Count, elapsed));
    }

    private async Task<IReadOnlyList<FolderChild>?> GetListingAsync(ResourceKey folder)
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

        var listing = enumerateResult.Value;
        _listings[folder] = listing;

        return listing;
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
            if (_listings.TryGetValue(parentFolder, out var parentListing)
                && !parentListing.Any(item => item.IsFolder && item.Resource == expandedFolder))
            {
                missingFolders.Add(expandedFolder);
            }
        }

        foreach (var missingFolder in missingFolders)
        {
            FolderStateService.SetExpanded(missingFolder, false);
        }
    }

    // Subscribes again whenever the listed folders change, before disposing the old subscription so no change is
    // missed between the two.
    private void UpdateSubscription()
    {
        if (_subscription is not null
            && _subscribedFolders.SetEquals(_listings.Keys))
        {
            return;
        }

        var listedFolders = _listings.Keys.ToHashSet();
        var matcher = new ListedFoldersMatcher(listedFolders);
        var handlers = new ResourceChangeHandlers(OnChanges, OnResync);

        var subscribeResult = ResourceService.Monitor.SubscribeToMatcher(ResourceKey.DefaultRoot, matcher, handlers);
        if (subscribeResult.IsFailure)
        {
            _logger.LogWarning(subscribeResult, "Failed to subscribe to the changes in the resource tree's folders");
            return;
        }

        var previousSubscription = _subscription;
        _subscription = subscribeResult.Value;
        _subscribedFolders = listedFolders;
        previousSubscription?.Dispose();
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
        var staleFolders = _listings.Keys.ToList();

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

    // A change makes a listed folder stale when the folder contains the changed item, is the changed item, or is
    // inside the changed item. Letter case is ignored, because a delete on macOS reports the name in the case the
    // caller used.
    private List<ResourceKey> FindStaleFolders(IReadOnlyList<ResourceChange> changes)
    {
        var staleFolders = new HashSet<ResourceKey>();

        foreach (var change in changes)
        {
            var changedPath = change.Resource.Path;
            var parentPath = change.Resource.GetParent().Path;

            foreach (var listedFolder in _listings.Keys)
            {
                var listedPath = listedFolder.Path;

                if (string.Equals(listedPath, parentPath, StringComparison.OrdinalIgnoreCase)
                    || ListedFoldersMatcher.IsAtOrInside(listedPath, changedPath))
                {
                    staleFolders.Add(listedFolder);
                }
            }
        }

        return staleFolders.ToList();
    }

    private bool IsVisible(FolderChild item)
    {
        if (ShowHiddenFiles)
        {
            return true;
        }

        return !ResourceTreeFilter.IsHidden(item.Resource, item.IsFolder, ResourceService.Policy);
    }

    // Returns true if the folder has a visible child. An unlisted folder counts as having one, so its row shows an
    // expander.
    private bool HasVisibleChildren(ResourceKey folder)
    {
        if (!_listings.TryGetValue(folder, out var listing))
        {
            return true;
        }

        return listing.Any(IsVisible);
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

        foreach (var item in listing)
        {
            var isHidden = ResourceTreeFilter.IsHidden(item.Resource, item.IsFolder, policy);
            if (isHidden
                && !showHiddenFiles)
            {
                continue;
            }

            var writableState = WritableStatePriority.Compute(item.Resource, item.Attributes, rootHandlers);
            var readOnlyMessage = ReadOnlyMessageHelper.GetReadOnlyMessage(writableState, _stringLocalizer) ?? string.Empty;

            if (item.IsFolder)
            {
                var isExpanded = FolderStateService.IsExpanded(item.Resource);
                var hasChildren = HasVisibleChildren(item.Resource);

                var appearance = new ResourceViewItemAppearance(
                    Name: item.Resource.ResourceName,
                    IndentLevel: indentLevel,
                    IsFolder: true,
                    HasChildren: hasChildren,
                    IsExpanded: isExpanded,
                    IsProjectFolder: false,
                    IsHidden: isHidden,
                    WritableState: writableState,
                    ReadOnlyMessage: readOnlyMessage,
                    ProjectFolderTooltip: string.Empty);
                builder.Add(item.Resource, appearance, null);

                if (isExpanded
                    && hasChildren)
                {
                    AddChildRows(builder, item.Resource, indentLevel + 1);
                }
            }
            else
            {
                var appearance = new ResourceViewItemAppearance(
                    Name: item.Resource.ResourceName,
                    IndentLevel: indentLevel,
                    IsFolder: false,
                    HasChildren: false,
                    IsExpanded: false,
                    IsProjectFolder: false,
                    IsHidden: isHidden,
                    WritableState: writableState,
                    ReadOnlyMessage: readOnlyMessage,
                    ProjectFolderTooltip: string.Empty);
                builder.Add(item.Resource, appearance, GetFileIcon);
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
