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
    /// Moves a list of resources to the specified folder. 
    /// </summary>
    public void MoveResources(List<IResource> resources, IFolderResource? newParent)
    {
        if (newParent is null)
        {
            newParent = _projectService.ResourceRegistry.RootFolder;
        }

        foreach (var resource in resources)
        {
            var fromResourceKey = _projectService.ResourceRegistry.GetResourceKey(resource);
            var parentResourceKey = _projectService.ResourceRegistry.GetResourceKey(newParent);
            var toResourceKey = parentResourceKey.IsEmpty ? resource.Name : parentResourceKey + "/" + resource.Name;

            if (fromResourceKey == toResourceKey)
            {
                // Moving a resource to the same location is technically a no-op, but we still need to update
                // the resource tree because the TreeView may now be displaying the resources in the wrong order.
                _commandService.Execute<IUpdateResourceTreeCommand>();
                continue;
            }

            _commandService.Execute<ICopyResourceCommand>(command =>
            {
                command.FromResourceKey = fromResourceKey;
                command.ToResourceKey = toResourceKey;
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
                var parentFolderPath = resourceRegistry.GetPath(parentFolder);
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
