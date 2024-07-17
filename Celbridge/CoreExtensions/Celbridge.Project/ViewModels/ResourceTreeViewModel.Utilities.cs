using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Workspace;
using Microsoft.Extensions.Localization;

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
        var resourceKey = resourceRegistry.GetResourceKey(folderResource);

        bool currentState = resourceRegistry.IsFolderExpanded(resourceKey);
        if (currentState == isExpanded)
        {
            return;
        }

        resourceRegistry.SetFolderIsExpanded(resourceKey, isExpanded);

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
            var sourceResourceKey = _projectService.ResourceRegistry.GetResourceKey(resource);
            var destResourceKey = _projectService.ResourceRegistry.GetResourceKey(parentFolder);

            if (sourceResourceKey == destResourceKey)
            {
                // Moving a resource to the same location is technically a no-op, but we still need to update
                // the resource tree because the TreeView may now be displaying the resources in the wrong order.
                _commandService.Execute<IUpdateResourceTreeCommand>();
                continue;
            }

            _commandService.Execute<ICopyResourceCommand>(command =>
            {
                command.SourceResourceKey = sourceResourceKey;
                command.DestResourceKey = destResourceKey;
                command.Operation = CopyResourceOperation.Move;
            });
        }
    }

    /// <summary>
    /// Find a localized default resource name that doesn't clash with an existing resource on disk. 
    /// </summary>
    private string FindDefaultResourceName(string stringKey, IFolderResource? parentFolder)
    {
        var resourceRegistry = _projectService.ResourceRegistry;

        string defaultResourceName;
        if (parentFolder is null)
        {
            defaultResourceName = _stringLocalizer.GetString(stringKey, 1).ToString();
        }
        else
        {
            int resourceNumber = 1;
            while (true)
            {
                var parentFolderPath = resourceRegistry.GetResourcePath(parentFolder);
                var candidateName = _stringLocalizer.GetString(stringKey, resourceNumber).ToString();
                var candidatePath = Path.Combine(parentFolderPath, candidateName);
                if (!Directory.Exists(candidatePath) &&
                    !File.Exists(candidatePath))
                {
                    defaultResourceName = candidateName;
                    break;
                }
                resourceNumber++;
            }
        }

        return defaultResourceName;
    }
}
