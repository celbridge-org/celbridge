using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;

namespace Celbridge.Project.ViewModels;

/// <summary>
/// Edit operations support for the resource tree view model.
/// </summary>
public partial class ResourceTreeViewModel
{
    public void ShowAddResourceDialog(ResourceType resourceType, IFolderResource? parentFolder)
    {
        var resourceRegistry = _projectService.ResourceRegistry;

        if (parentFolder is null)
        {
            // If the parent folder is null, add the new folder to the root folder
            parentFolder = resourceRegistry.RootFolder;
        }

        var parentFolderResource = resourceRegistry.GetResourceKey(parentFolder);

        // Execute a command to show the add resource dialog
        _commandService.Execute<IAddResourceDialogCommand>(command =>
        {
            command.ResourceType = resourceType;
            command.ParentFolderResource = parentFolderResource;
        });
    }

    public void ShowDeleteResourceDialog(IResource resource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var resourceKey = resourceRegistry.GetResourceKey(resource);

        // Execute a command to show the delete resource dialog
        _commandService.Execute<IDeleteResourceDialogCommand>(command =>
        {
            command.Resource = resourceKey;
        });
    }

    public void ShowRenameResourceDialog(IResource resource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var resourceKey = resourceRegistry.GetResourceKey(resource);

        // Execute a command to show the rename resource dialog
        _commandService.Execute<IRenameResourceDialogCommand>(command =>
        {
            command.Resource = resourceKey;
        });
    }
}
