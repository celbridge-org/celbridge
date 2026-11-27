using Celbridge.Dialog;
using Celbridge.Patterns;
using Celbridge.Utilities;

namespace Celbridge.UserInterface.ViewModels.Controls;

/// <summary>
/// Decides which resources the resource picker lists, and builds the item for each one. The factory never changes
/// after construction.
/// </summary>
internal sealed class ResourcePickerItemFactory
{
    private readonly ResourcePickerMode _mode;
    private readonly ResourcePickerExtensions _extensions;
    private readonly bool _showHiddenFiles;
    private readonly IResourcePolicy _policy;
    private readonly IRootHandlerRegistry _rootHandlerRegistry;
    private readonly IIconService _iconService;
    private readonly IReadOnlyDictionary<WritableState, string> _readOnlyMessages;

    public ResourcePickerItemFactory(
        ResourcePickerMode mode,
        ResourcePickerExtensions extensions,
        bool showHiddenFiles,
        IResourcePolicy policy,
        IRootHandlerRegistry rootHandlerRegistry,
        IIconService iconService,
        IReadOnlyDictionary<WritableState, string> readOnlyMessages)
    {
        _mode = mode;
        _extensions = extensions;
        _showHiddenFiles = showHiddenFiles;
        _policy = policy;
        _rootHandlerRegistry = rootHandlerRegistry;
        _iconService = iconService;
        _readOnlyMessages = readOnlyMessages;
    }

    /// <summary>
    /// Returns true if the mode and the extensions let the picker list the resource. Folder mode lists folders only.
    /// Open and save modes list folders and the files with an allowed extension.
    /// </summary>
    public bool IsListedKind(ResourceKey resource, bool isFolder)
    {
        if (isFolder)
        {
            return true;
        }

        if (_mode == ResourcePickerMode.Folder)
        {
            return false;
        }

        return _extensions.IsAllowed(resource.ResourceName);
    }

    /// <summary>
    /// Returns true if the picker shows the resource. The picker leaves out what the Explorer hides, unless the
    /// Explorer's Show Hidden Files toggle is on.
    /// </summary>
    public bool IsShown(ResourceKey resource, bool isFolder)
    {
        var isHidden = _policy.IsExplorerHidden(resource, isFolder);
        return IsShown(isHidden);
    }

    /// <summary>
    /// Returns the item for a resource, or null when the picker leaves the resource out.
    /// </summary>
    public ResourcePickerItem? CreateItem(
        ResourceKey resource,
        bool isFolder,
        FileSystemAttributes attributes,
        bool showsPath)
    {
        if (!IsListedKind(resource, isFolder))
        {
            return null;
        }

        var isHidden = _policy.IsExplorerHidden(resource, isFolder);
        if (!IsShown(isHidden))
        {
            return null;
        }

        var writableState = WritableStatePriority.Compute(resource, attributes, _rootHandlerRegistry);
        var readOnlyMessage = _readOnlyMessages.GetValueOrDefault(writableState, string.Empty);
        var state = new ResourcePickerItemState(isHidden, writableState, readOnlyMessage);

        var fileIcon = isFolder
            ? null
            : _iconService.GetFileIconForFileName(resource.ResourceName);

        return new ResourcePickerItem(resource, isFolder, fileIcon, state, showsPath);
    }

    /// <summary>
    /// Returns the filter for the search's walk of the project, or null when the walk takes every resource. The
    /// filter leaves out what the Explorer hides.
    /// </summary>
    public IPathMatcher? GetWalkFilter()
    {
        if (_showHiddenFiles)
        {
            return null;
        }

        return new ExplorerShownPathMatcher(_policy);
    }

    private bool IsShown(bool isHidden)
    {
        return !isHidden
            || _showHiddenFiles;
    }

    // Matches each path shown in the Explorer. The walk calls only CanMatchInside, so the walk skips the contents of
    // each hidden folder.
    private sealed class ExplorerShownPathMatcher : IPathMatcher
    {
        private readonly IResourcePolicy _policy;

        public ExplorerShownPathMatcher(IResourcePolicy policy)
        {
            _policy = policy;
        }

        public bool IsMatch(string path, bool isFolder)
        {
            var resource = new ResourceKey(path);
            return !_policy.IsExplorerHidden(resource, isFolder);
        }

        public bool CanMatchInside(string folderPath)
        {
            var folder = new ResourceKey(folderPath);
            return !_policy.IsExplorerHidden(folder, isFolder: true);
        }
    }
}
