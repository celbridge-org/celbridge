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
    public void AddFolder(IFolderResource? parentFolder)
    {
        var resourceRegistry = _projectService.ResourceRegistry;

        if (parentFolder is null)
        {
            // If the parent folder is null, add the new folder to the root folder
            parentFolder = resourceRegistry.RootFolder;
        }

        var parentFolderResourceKey = resourceRegistry.GetResourceKey(parentFolder);

        async Task ShowDialogAsync()
        {
            var defaultText = FindDefaultResourceName("ResourceTree_DefaultFolderName", parentFolder);

            var validator = _serviceProvider.GetRequiredService<IResourceNameValidator>();
            validator.ParentFolder = parentFolder;

            var showResult = await _dialogService.ShowInputTextDialogAsync(
                AddFolderString,
                EnterNameString,
                defaultText,
                ..,
                validator);
            if (showResult.IsSuccess)
            {
                var inputText = showResult.Value;

                // Execute a command to add the folder resource
                var addResourceKey = parentFolderResourceKey.IsEmpty ? showResult.Value : $"{parentFolderResourceKey}/{showResult.Value}";
                _commandService.Execute<IAddFolderCommand>(command => command.ResourceKey = addResourceKey);

                // Execute a command to update the resource tree
                _commandService.Execute<IUpdateResourceTreeCommand>();
            }
        }

        _ = ShowDialogAsync();
    }

    public void AddFile(IFolderResource? parentFolder)
    {
        var resourceRegistry = _projectService.ResourceRegistry;

        if (parentFolder is null)
        {
            // If the parent folder is null, add the new file to the root folder
            parentFolder = resourceRegistry.RootFolder;
        }

        var parentFolderResourceKey = parentFolder is null ? new ResourceKey(string.Empty) : resourceRegistry.GetResourceKey(parentFolder);

        async Task ShowDialogAsync()
        {
            var defaultText = FindDefaultResourceName("ResourceTree_DefaultFileName", parentFolder);

            var validator = _serviceProvider.GetRequiredService<IResourceNameValidator>();
            validator.ParentFolder = parentFolder;

            var showResult = await _dialogService.ShowInputTextDialogAsync(
                AddFileString,
                EnterNameString,
                defaultText,
                .., // Select the entire range
                validator);
            if (showResult.IsSuccess)
            {
                var inputText = showResult.Value;

                // Execute a command to add the file resource
                var addResourceKey = parentFolderResourceKey.IsEmpty ? showResult.Value : $"{parentFolderResourceKey}/{showResult.Value}";
                _commandService.Execute<IAddFileCommand>(command => command.ResourceKey = addResourceKey);

                // Execute a command to update the resource tree
                _commandService.Execute<IUpdateResourceTreeCommand>();
            }
        }

        _ = ShowDialogAsync();
    }

    public void DeleteFolder(IFolderResource folderResource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var resourceKey = resourceRegistry.GetResourceKey(folderResource);

        var resourceName = folderResource.Name;
        var confirmDeleteFolderString = _stringLocalizer.GetString("ResourceTree_ConfirmDeleteFolder", resourceName);

        async Task ShowDialogAsync()
        {
            var showResult = await _dialogService.ShowConfirmationDialogAsync(DeleteString, confirmDeleteFolderString);
            if (showResult.IsSuccess)
            {
                var confirmed = showResult.Value;
                if (confirmed)
                {
                    // Execute a command to delete the folder resource
                    _commandService.Execute<IDeleteResourceCommand>(command => command.ResourceKey = resourceKey);

                    // Execute a command to update the resource tree
                    _commandService.Execute<IUpdateResourceTreeCommand>();
                }
            }
        }

        _ = ShowDialogAsync();
    }

    public void DeleteFile(IFileResource fileResource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var resourceKey = resourceRegistry.GetResourceKey(fileResource);

        var resourceName = fileResource.Name;
        var confirmDeleteFileString = _stringLocalizer.GetString("ResourceTree_ConfirmDeleteFile", resourceName);

        async Task ShowDialogAsync()
        {
            var showResult = await _dialogService.ShowConfirmationDialogAsync(DeleteString, confirmDeleteFileString);
            if (showResult.IsSuccess)
            {
                var confirmed = showResult.Value;
                if (confirmed)
                {
                    // Execute a command to delete the file resource
                    _commandService.Execute<IDeleteResourceCommand>(command => command.ResourceKey = resourceKey);

                    // Execute a command to update the resource tree
                    _commandService.Execute<IUpdateResourceTreeCommand>();
                }
            }
        }

        _ = ShowDialogAsync();
    }

    public void RenameFolder(IFolderResource folderResource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var resourceKey = resourceRegistry.GetResourceKey(folderResource);

        var resourceName = folderResource.Name;

        async Task ShowDialogAsync()
        {
            var renameResourceString = _stringLocalizer.GetString("ResourceTree_RenameResource", resourceName);

            var defaultText = resourceName;

            var validator = _serviceProvider.GetRequiredService<IResourceNameValidator>();
            validator.ParentFolder = folderResource.ParentFolder;
            validator.ValidNames.Add(resourceName); // The original name is always valid when renaming

            var showResult = await _dialogService.ShowInputTextDialogAsync(
                renameResourceString,
                EnterNewNameString,
                defaultText,
                .., // Select the entire range
                validator);
            if (showResult.IsSuccess)
            {
                var inputText = showResult.Value;

                var fromResourceKey = resourceKey;
                var parentResourceKey = resourceKey.GetParent();
                var toResourceKey = parentResourceKey.IsEmpty ? inputText : parentResourceKey + "/" + inputText;

                // Maintain the expanded state of the folder after renaming
                bool wasExpanded = resourceRegistry.IsFolderExpanded(resourceKey);

                // Execute a command to move the folder resource to perform the rename
                _commandService.Execute<IMoveResourceCommand>(command =>
                {
                    command.FromResourceKey = fromResourceKey;
                    command.ToResourceKey = toResourceKey;
                    command.ExpandMovedFolder = wasExpanded;
                });

                // Execute a command to update the resource tree
                _commandService.Execute<IUpdateResourceTreeCommand>();
            }
        }

        _ = ShowDialogAsync();
    }

    public void RenameFile(IFileResource fileResource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var resourceKey = resourceRegistry.GetResourceKey(fileResource);

        var resourceName = fileResource.Name;

        async Task ShowDialogAsync()
        {
            var renameResourceString = _stringLocalizer.GetString("ResourceTree_RenameResource", resourceName);

            var defaultText = resourceName;
            var selectedRange = new Range(0, Path.GetFileNameWithoutExtension(resourceName).Length); // Don't select extension

            var validator = _serviceProvider.GetRequiredService<IResourceNameValidator>();
            validator.ParentFolder = fileResource.ParentFolder;
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

                var fromResourceKey = resourceKey;
                var parentResourceKey = resourceKey.GetParent();
                var toResourceKey = parentResourceKey.IsEmpty ? inputText : parentResourceKey + "/" + inputText;

                // Execute a command to move the file resource to perform the rename
                _commandService.Execute<IMoveResourceCommand>(command =>
                {
                    command.FromResourceKey = fromResourceKey;
                    command.ToResourceKey = toResourceKey;
                });

                // Execute a command to update the resource tree
                _commandService.Execute<IUpdateResourceTreeCommand>();
            }
        }

        _ = ShowDialogAsync();
    }
}
