using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;

namespace Celbridge.Project.ViewModels;

/// <summary>
/// Clipboard operation support for the resource tree view model.
/// </summary>
public partial class ResourceTreeViewModel
{
    public void CutResource(IResource resource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;

        var resourceKey = resourceRegistry.GetResourceKey(resource);

        // Execute a command to cut the resource to the clipboard
        _commandService.Execute<ICopyResourceCommand>(command =>
        {
            command.ResourceKey = resourceKey;
            command.Move = true;
        });
    }

    public void CopyResource(IResource resource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;

        var resourceKey = resourceRegistry.GetResourceKey(resource);

        // Execute a command to copy the resource to the clipboard
        _commandService.Execute<ICopyResourceCommand>(command => command.ResourceKey = resourceKey);
    }

    public void PasteResource(IResource? resource)
    {
        var rootFolder = _projectService.ResourceRegistry.RootFolder;

        IFolderResource pasteFolder;
        if (resource is IFileResource fileResource)
        {
            pasteFolder = fileResource.ParentFolder ?? rootFolder;
        }
        else if (resource is IFolderResource folderResource)
        {
            pasteFolder = folderResource ?? rootFolder;
        }
        else
        {
            pasteFolder = rootFolder;
        }

        var folderResourceKey = _projectService.ResourceRegistry.GetResourceKey(pasteFolder);

        // Execute a command to paste the clipboard content to the folder resource
        _commandService.Execute<IPasteResourceCommand>(command => command.FolderResourceKey = folderResourceKey);
    }
}