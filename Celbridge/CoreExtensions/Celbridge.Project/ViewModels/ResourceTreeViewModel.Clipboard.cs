using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;

namespace Celbridge.Project.ViewModels;

/// <summary>
/// Clipboard operation support for the resource tree view model.
/// </summary>
public partial class ResourceTreeViewModel
{
    public void CutResourceToClipboard(IResource resource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;

        var resourceKey = resourceRegistry.GetResourceKey(resource);

        // Execute a command to cut the resource to the clipboard
        _commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.Resource = resourceKey;
            command.MoveResource = true;
        });
    }

    public void CopyResourceToClipboard(IResource resource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;

        var resourceKey = resourceRegistry.GetResourceKey(resource);

        // Execute a command to copy the resource to the clipboard
        _commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.Resource = resourceKey;
        });
    }

    public void PasteResourceFromClipboard(IResource? resource)
    {
        var rootFolder = _projectService.ResourceRegistry.RootFolder;

        IFolderResource pasteFolder;
        if (resource is IFileResource file)
        {
            // Paste to the parent folder of the file resource
            pasteFolder = file.ParentFolder ?? rootFolder;
        }
        else if (resource is IFolderResource folder)
        {
            // Paste to the folder resource
            pasteFolder = folder ?? rootFolder;
        }
        else
        {
            // Paste to the root folder
            pasteFolder = rootFolder;
        }

        var folderResource = _projectService.ResourceRegistry.GetResourceKey(pasteFolder);

        // Execute a command to paste the clipboard content to the folder resource
        _commandService.Execute<IPasteResourceFromClipboardCommand>(command =>
        {
            command.FolderResource = folderResource;
        });
    }
}