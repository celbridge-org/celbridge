using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Workspace;

namespace Celbridge.Project.ViewModels;

/// <summary>
/// Supporting utilities for the resource tree view model.
/// </summary>
public partial class ResourceTreeViewModel
{
    /// <summary>
    /// Sets the specific folder to be expanded. 
    /// </summary>
    public void SetFolderIsExpanded(IFolderResource folderResource, bool isExpanded)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var resource = resourceRegistry.GetResourceKey(folderResource);

        bool currentState = resourceRegistry.IsFolderExpanded(resource);
        if (currentState == isExpanded)
        {
            return;
        }

        resourceRegistry.SetFolderIsExpanded(resource, isExpanded);

        // Save the workspace data (with a delay) to ensure the new expanded state is persisted
        _commandService.RemoveCommandsOfType<ISaveWorkspaceStateCommand>();
        _commandService.Execute<ISaveWorkspaceStateCommand>(250);
    }

    /// <summary>
    /// Moves a list of resources to the specified parent folder. 
    /// </summary>
    public void MoveResources(List<IResource> resources, IFolderResource? parentFolder)
    {
        if (parentFolder is null)
        {
            // A null folder reference indicates the root folder
            parentFolder = _projectService.ResourceRegistry.RootFolder;
        }

        foreach (var resource in resources)
        {
            var sourceResource = _projectService.ResourceRegistry.GetResourceKey(resource);
            var destResource = _projectService.ResourceRegistry.GetResourceKey(parentFolder);

            if (sourceResource == destResource)
            {
                // Moving a resource to the same location is technically a no-op, but we still need to update
                // the resource tree because the TreeView may now be displaying the resources in the wrong order.
                _commandService.Execute<IUpdateResourceTreeCommand>();
                continue;
            }

            _commandService.Execute<ICopyResourceCommand>(command =>
            {
                command.SourceResource = sourceResource;
                command.DestResource = destResource;
                command.Operation = CopyResourceOperation.Move;
            });
        }
    }
}
