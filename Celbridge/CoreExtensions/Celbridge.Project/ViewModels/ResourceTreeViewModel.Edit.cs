using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Validators;
using Microsoft.Extensions.Localization;

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

        var parentFolderResourceKey = resourceRegistry.GetResourceKey(parentFolder);

        // Execute a command to add the folder resource
        _commandService.Execute<IShowAddResourceDialogCommand>(command =>
        {
            command.ResourceType = resourceType;
            command.ParentFolderResourceKey = parentFolderResourceKey;
        });
    }

    public void ShowDeleteResourceDialog(IResource resource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var resourceKey = resourceRegistry.GetResourceKey(resource);
        var resourceName = resource.Name;

        string confirmDeleteStringKey;
        switch (resource)
        {
            case IFileResource:
                confirmDeleteStringKey = "ResourceTree_ConfirmDeleteFile";
                break;
            case IFolderResource:
                confirmDeleteStringKey = "ResourceTree_ConfirmDeleteFolder";
                break;
            default:
                throw new ArgumentException();
        }

        var confirmDeleteString = _stringLocalizer.GetString(confirmDeleteStringKey, resourceName);

        async Task ShowDialogAsync()
        {
            var showResult = await _dialogService.ShowConfirmationDialogAsync(DeleteString, confirmDeleteString);
            if (showResult.IsSuccess)
            {
                var confirmed = showResult.Value;
                if (confirmed)
                {
                    // Execute a command to delete the folder resource
                    _commandService.Execute<IDeleteResourceCommand>(command =>
                    {
                        command.ResourceKey = resourceKey;
                    });

                    var message = new RequestResourceTreeUpdateMessage();
                    _messengerService.Send(message);
                }
            }
        }

        _ = ShowDialogAsync();
    }

    public void ShowRenameResourceDialog(IResource resource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var resourceKey = resourceRegistry.GetResourceKey(resource);
        var resourceName = resource.Name;

        Range selectedRange;
        switch (resource)
        {
            case IFileResource:
                selectedRange = new Range(0, Path.GetFileNameWithoutExtension(resourceName).Length); // Don't select extension
                break;
            case IFolderResource:
                selectedRange = new Range(0, resourceName.Length);
                break;
            default:
                throw new ArgumentException();
        }

        async Task ShowDialogAsync()
        {
            var renameResourceString = _stringLocalizer.GetString("ResourceTree_RenameResource", resourceName);

            var defaultText = resourceName;

            var validator = _serviceProvider.GetRequiredService<IResourceNameValidator>();
            validator.ParentFolder = resource.ParentFolder;
            validator.ValidNames.Add(resourceName); // The original name is always valid when renaming

            var showResult = await _dialogService.ShowInputTextDialogAsync(
                renameResourceString,
                EnterNewNameString,
                defaultText,
                selectedRange,
                validator);
            if (showResult.IsSuccess)
            {
                var inputText = showResult.Value;

                var sourceResourceKey = resourceKey;
                var parentResourceKey = resourceKey.GetParent();
                var destResourceKey = parentResourceKey.IsEmpty ? (ResourceKey)inputText : parentResourceKey.Combine(inputText);

                bool isFolderResource = resource is IFolderResource;

                // Maintain the expanded state of folders after rename
                bool isExpandedFolder = isFolderResource && 
                    resourceRegistry.IsFolderExpanded(resourceKey);

                // Execute a command to move the folder resource to perform the rename
                _commandService.Execute<ICopyResourceCommand>(command =>
                {
                    command.SourceResourceKey = sourceResourceKey;
                    command.DestResourceKey = destResourceKey;
                    command.Operation = CopyResourceOperation.Move;

                    if (isExpandedFolder)
                    {
                        command.ExpandCopiedFolder = true;
                    }
                });

                var message = new RequestResourceTreeUpdateMessage();
                _messengerService.Send(message);
            }
        }

        _ = ShowDialogAsync();
    }
}
