using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Validators;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.Project.Models;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;
using System.Collections.ObjectModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Celbridge.Project.ViewModels;

public partial class ResourceTreeViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IProjectService _projectService;
    private readonly ICommandService _commandService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public ObservableCollection<IResource> Resources => _projectService.ResourceRegistry.RootFolder.Children;

    private LocalizedString AddFolderString => _stringLocalizer.GetString("ResourceTree_AddFolder");
    private LocalizedString AddFileString => _stringLocalizer.GetString("ResourceTree_AddFile");
    private LocalizedString EnterNameString => _stringLocalizer.GetString("ResourceTree_EnterName");
    private LocalizedString DeleteString => _stringLocalizer.GetString("ResourceTree_Delete");
    private LocalizedString EnterNewNameString => _stringLocalizer.GetString("ResourceTree_EnterNewName");

    private bool _resourceTreeUpdatePending;

    public ResourceTreeViewModel(
        IMessengerService messengerService,
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService,
        IDialogService dialogService,
        IStringLocalizer stringLocalizer)
    {
        _serviceProvider = serviceProvider;
        _projectService = workspaceWrapper.WorkspaceService.ProjectService;
        _commandService = commandService;
        _dialogService = dialogService;
        _stringLocalizer = stringLocalizer;

        // Listen for messages to determine when to update the resource tree
        messengerService.Register<RequestResourceTreeUpdate>(this, OnRequestResourceTreeUpdateMessage);
        messengerService.Register<ExecutedCommandMessage>(this, OnExecutedCommandMessage); 
    }

    private void OnRequestResourceTreeUpdateMessage(object recipient, RequestResourceTreeUpdate message)
    {
        // Set a flag to update the resource tree
        _resourceTreeUpdatePending = true;
    }

    private void OnExecutedCommandMessage(object recipient, ExecutedCommandMessage message)
    {
        if (_resourceTreeUpdatePending)
        {
            // Don't update if there are any pending resource tree commands
            if (!_commandService.ContainsCommandsOfType<IAddFileCommand>() &&
                !_commandService.ContainsCommandsOfType<IAddFolderCommand>() &&
                !_commandService.ContainsCommandsOfType<IDeleteFileCommand>() &&
                !_commandService.ContainsCommandsOfType<IDeleteFolderCommand>() &&
                !_commandService.ContainsCommandsOfType<IMoveFileCommand>() &&
                !_commandService.ContainsCommandsOfType<IMoveFolderCommand>() &&
                !_commandService.ContainsCommandsOfType<IUpdateResourceTreeCommand>())
            {
                // Execute a command to update the resource tree
                _commandService.Execute<IUpdateResourceTreeCommand>();
                _resourceTreeUpdatePending = false;
            }
        }
    }

    public void SetFolderIsExpanded(IFolderResource folderResource, bool isExpanded)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var resourcePath = resourceRegistry.GetResourcePath(folderResource);

        bool currentState = resourceRegistry.IsFolderExpanded(resourcePath);
        if (currentState == isExpanded)
        {
            return;
        }

        resourceRegistry.SetFolderIsExpanded(resourcePath, isExpanded);

        // Save the workspace data (with a delay) to ensure the new expanded state is persisted
        _commandService.RemoveCommandsOfType<ISaveWorkspaceStateCommand>();
        _commandService.Execute<ISaveWorkspaceStateCommand>(250);
    }

    public void AddFolder(IFolderResource? parentFolder)
    {
        if (parentFolder is null)
        {
            // If the parent folder is null, add the new folder to the root folder
            var getResult = _projectService.ResourceRegistry.GetResource(string.Empty);
            Guard.IsTrue(getResult.IsSuccess);
            parentFolder = getResult.Value as IFolderResource;
        }

        var resourceRegistry = _projectService.ResourceRegistry;
        var path = parentFolder is null ? string.Empty : resourceRegistry.GetResourcePath(parentFolder);

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
                var resourcePath = string.IsNullOrEmpty(path) ? showResult.Value : $"{path}/{showResult.Value}";
                _commandService.Execute<IAddFolderCommand>(command => command.ResourcePath = resourcePath);

                // Execute a command to update the resource tree
                _commandService.Execute<IUpdateResourceTreeCommand>();
            }
        }

        _ = ShowDialogAsync();
    }

    public void AddFile(IFolderResource? parentFolder)
    {
        if (parentFolder is null)
        {
            // If the parent folder is null, add the new file to the root folder
            var getResult = _projectService.ResourceRegistry.GetResource(string.Empty);
            Guard.IsTrue(getResult.IsSuccess);
            parentFolder = getResult.Value as IFolderResource;
        }

        var resourceRegistry = _projectService.ResourceRegistry;
        var path = parentFolder is null ? string.Empty : resourceRegistry.GetResourcePath(parentFolder);

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
                var resourcePath = string.IsNullOrEmpty(path) ? showResult.Value : $"{path}/{showResult.Value}";
                _commandService.Execute<IAddFileCommand>(command => command.ResourcePath = resourcePath);

                // Execute a command to update the resource tree
                _commandService.Execute<IUpdateResourceTreeCommand>();
            }
        }

        _ = ShowDialogAsync();
    }

    public void CutResource(IResource resource)
    {
    }

    public void CopyResource(IResource resource)
    {
        //async Task CopyFile(string filePath)
        //{
        //    StorageFile file = await StorageFile.GetFileFromPathAsync(filePath);
        //    if (file != null)
        //    {
        //        var dataPackage = new DataPackage();
        //        dataPackage.RequestedOperation = DataPackageOperation.Copy;

        //        List<IStorageItem> items = new List<IStorageItem>();
        //        items.Add(file);
        //        dataPackage.SetStorageItems(items);

        //        Clipboard.SetContent(dataPackage);
        //        Clipboard.Flush();
        //    }
        //}

        //var path = _projectService.ResourceRegistry.GetPath(resource);
        //if (string.IsNullOrEmpty(path))
        //{
        //    return;
        //}

        //if (resource is IFileResource)
        //{
        //    _ = CopyFile(path);
        //}
    }

    public void PasteResource(IResource resource)
    {
        //async Task PasteFile(string folderPath)
        //{
        //    DataPackageView dataPackageView = Clipboard.GetContent();
        //    if (dataPackageView.Contains(StandardDataFormats.StorageItems))
        //    {
        //        IReadOnlyList<IStorageItem> storageItems = await dataPackageView.GetStorageItemsAsync();

        //        if (storageItems.Count > 0)
        //        {
        //            var storageFile = storageItems[0] as StorageFile;
        //            if (storageFile != null)
        //            {
        //                // Save the file to the parent folder
        //                var sourcePath = storageFile.Path;
        //                //File.Copy(sourcePath, folderPath);
        //            }
        //        }
        //    }
        //}

        //IFolderResource parentFolder;
        //if (resource is IFileResource fileResource)
        //{
        //    parentFolder = fileResource.ParentFolder!;
        //}
        //else if (resource is IFolderResource folderResource)
        //{
        //    parentFolder = folderResource;
        //}
        //else
        //{
        //    parentFolder = _projectService.ResourceRegistry.RootFolder;
        //}

        //var folderPath = _projectService.ResourceRegistry.GetPath(parentFolder);
        //if (string.IsNullOrEmpty(folderPath))
        //{
        //    return;
        //}

        //_ = PasteFile(folderPath);
    }

    public void DeleteFolder(FolderResource folderResource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var resourcePath = resourceRegistry.GetResourcePath(folderResource);

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
                    _commandService.Execute<IDeleteFolderCommand>(command => command.ResourcePath = resourcePath);

                    // Execute a command to update the resource tree
                    _commandService.Execute<IUpdateResourceTreeCommand>();
                }
            }
        }

        _ = ShowDialogAsync();
    }

    public void DeleteFile(FileResource fileResource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var resourcePath = resourceRegistry.GetResourcePath(fileResource);

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
                    _commandService.Execute<IDeleteFileCommand>(command => command.ResourcePath = resourcePath);

                    // Execute a command to update the resource tree
                    _commandService.Execute<IUpdateResourceTreeCommand>();
                }
            }
        }

        _ = ShowDialogAsync();
    }

    public void RenameFolder(FolderResource folderResource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var resourcePath = resourceRegistry.GetResourcePath(folderResource);

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

                var fromResourcePath = resourcePath;
                var toResourcePath = Path.GetDirectoryName(resourcePath);
                toResourcePath = string.IsNullOrEmpty(toResourcePath) ? inputText : toResourcePath + "/" + inputText;

                // Execute a command to move the folder resource to perform the rename
                _commandService.Execute<IMoveFolderCommand>(command =>
                {
                    command.FromResourcePath = fromResourcePath;
                    command.ToResourcePath = toResourcePath;
                });

                // Execute a command to update the resource tree
                _commandService.Execute<IUpdateResourceTreeCommand>();
            }
        }

        _ = ShowDialogAsync();
    }

    public void RenameFile(FileResource fileResource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var resourcePath = resourceRegistry.GetResourcePath(fileResource);

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

                var fromResourcePath = resourcePath;
                var toResourcePath = Path.GetDirectoryName(resourcePath);
                toResourcePath = string.IsNullOrEmpty(toResourcePath) ? inputText : toResourcePath + "/" + inputText;

                // Execute a command to move the file resource to perform the rename
                _commandService.Execute<IMoveFileCommand>(command =>
                {
                    command.FromResourcePath = fromResourcePath;
                    command.ToResourcePath = toResourcePath;
                });

                // Execute a command to update the resource tree
                _commandService.Execute<IUpdateResourceTreeCommand>();
            }
        }

        _ = ShowDialogAsync();
    }

    // Find a localized default resource name that doesn't clash with an existing resource on disk.
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

    public void MoveResources(List<IResource> resources, IFolderResource? newParent)
    {
        if (newParent is null)
        {
            // Todo: Should we move this logic into the ResourceRegistry?
            // If newParent is null, use the root folder as the parent
            newParent = _projectService.ResourceRegistry.GetResource(string.Empty).Value as IFolderResource;
        }
        Guard.IsNotNull(newParent);

        foreach (var resource in resources)
        {
            var fromResourcePath = _projectService.ResourceRegistry.GetResourcePath(resource);
            var toResourcePath = _projectService.ResourceRegistry.GetResourcePath(newParent);
            toResourcePath = string.IsNullOrEmpty(toResourcePath) ? resource.Name : toResourcePath + "/" + resource.Name;

            if (fromResourcePath == toResourcePath)
            {
                // Moving a resource to the same location is technically a no-op, but we still need to update
                // the resource tree because the TreeView may now be displaying the resources in the wrong order.
                _commandService.Execute<IUpdateResourceTreeCommand>();
                continue;
            }

            if (resource is IFileResource)
            {
                _commandService.Execute<IMoveFileCommand>(command =>
                {
                    command.FromResourcePath = fromResourcePath;
                    command.ToResourcePath = toResourcePath;
                });
            }
            else if (resource is IFolderResource)
            {
                _commandService.Execute<IMoveFolderCommand>(command =>
                {
                    command.FromResourcePath = fromResourcePath;
                    command.ToResourcePath = toResourcePath;
                });
            }
        }
    }
}
