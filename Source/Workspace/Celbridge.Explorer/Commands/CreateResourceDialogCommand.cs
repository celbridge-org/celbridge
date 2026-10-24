using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Utilities;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Commands;

public class CreateResourceDialogCommand : CommandBase, ICreateResourceDialogCommand
{
    private const string NewFolderTitleKey = "ResourceTree_NewFolder";
    private const string FolderNameKey = "NewFolderDialog_FolderName";
    private const string DefaultFolderNameKey = "ResourceTree_DefaultFolderName";
    private const string DefaultFileNameKey = "ResourceTree_DefaultFileName";
    private const string CreateButtonKey = "DialogButton_Create";

    public override CommandFlags CommandFlags => CommandFlags.None;

    public ResourceType ResourceType { get; set; }
    public ResourceKey DestFolderResource { get; set; }

    private readonly IServiceProvider _serviceProvider;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IDialogService _dialogService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public CreateResourceDialogCommand(
        IServiceProvider serviceProvider,
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IDialogService dialogService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _serviceProvider = serviceProvider;
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
        _dialogService = dialogService;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        // When invoked without a destination (e.g. from the application menu), default to the Explorer's
        // selected folder, falling back to the project root. The toolbar and context-menu callers always
        // supply DestFolderResource explicitly, so this only fills the gap for the menu entry points.
        if (DestFolderResource.IsEmpty
            && _workspaceWrapper.IsWorkspaceLoaded)
        {
            DestFolderResource = await ResolveDefaultDestinationFolderAsync();
        }

        if (ResourceType == ResourceType.File)
        {
            return await ShowNewFileDialogAsync();
        }
        else
        {
            return await ShowNewFolderDialogAsync();
        }
    }

    private async Task<ResourceKey> ResolveDefaultDestinationFolderAsync()
    {
        var explorerService = _workspaceWrapper.WorkspaceService.ExplorerService;
        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        var selectedResource = explorerService.SelectedResource;
        if (selectedResource.IsEmpty)
        {
            return ResourceKey.Empty;
        }

        var infoResult = await resourceFileSystem.GetInfoAsync(selectedResource);
        if (infoResult.IsFailure)
        {
            return ResourceKey.Empty;
        }

        var itemKind = infoResult.Value.Kind;
        if (itemKind == StorageItemKind.Folder)
        {
            return selectedResource;
        }

        if (itemKind == StorageItemKind.File)
        {
            return selectedResource.GetParent();
        }

        return ResourceKey.Empty;
    }

    private async Task<Result> ShowNewFileDialogAsync()
    {
        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return Result.Fail($"Failed to show new file dialog because workspace is not loaded");
        }

        var validator = _serviceProvider.GetRequiredService<IResourceNameValidator>();
        var setParentResult = await validator.SetParentFolderAsync(DestFolderResource);
        if (setParentResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve destination folder: '{DestFolderResource}'")
                .WithErrors(setParentResult);
        }
        validator.ValidateAsFolder = false;

        var defaultFileName = await FindDefaultFileNameAsync(DestFolderResource);

        var selectionRange = ResourceNameHelper.GetNameSelectionRange(defaultFileName);

        var showResult = await _dialogService.ShowNewFileDialogAsync(
            defaultFileName,
            selectionRange,
            validator);

        if (showResult.IsSuccess)
        {
            var config = showResult.Value;
            var newResource = DestFolderResource.Combine(config.FileName);

            // Execute a command to create the resource
            _commandService.Execute<ICreateResourceCommand>(command =>
            {
                command.ResourceType = ResourceType.File;
                command.DestResource = newResource;
                command.OpenAfterCreating = true;
            });
        }

        return Result.Ok();
    }

    private async Task<Result> ShowNewFolderDialogAsync()
    {
        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return Result.Fail($"Failed to show new folder dialog because workspace is not loaded");
        }

        var validator = _serviceProvider.GetRequiredService<IResourceNameValidator>();
        var setParentResult = await validator.SetParentFolderAsync(DestFolderResource);
        if (setParentResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve destination folder: '{DestFolderResource}'")
                .WithErrors(setParentResult);
        }
        validator.ValidateAsFolder = true;

        var defaultText = await FindDefaultFolderNameAsync(DestFolderResource);

        var titleString = _stringLocalizer.GetString(NewFolderTitleKey);
        var nameString = _stringLocalizer.GetString(FolderNameKey);

        // Select the entire folder name
        var selectionRange = ..;

        var showResult = await _dialogService.ShowInputTextDialogAsync(
            titleString,
            nameString,
            defaultText,
            selectionRange,
            validator,
            CreateButtonKey);

        if (showResult.IsSuccess)
        {
            var inputText = showResult.Value;

            var newResource = DestFolderResource.Combine(inputText);

            // Execute a command to create the resource
            _commandService.Execute<ICreateResourceCommand>(command =>
            {
                command.ResourceType = ResourceType.Folder;
                command.DestResource = newResource;
                command.OpenAfterCreating = false;
            });
        }

        return Result.Ok();
    }

    /// <summary>
    /// Find a default folder name that doesn't clash with an existing folder on disk.
    /// </summary>
    private async Task<string> FindDefaultFolderNameAsync(ResourceKey parentFolderKey)
    {
        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        string defaultFolderName = string.Empty;
        int folderNumber = 1;
        while (true)
        {
            var candidateName = _stringLocalizer.GetString(DefaultFolderNameKey, folderNumber).ToString();

            var candidateKey = parentFolderKey.Combine(candidateName);
            var infoResult = await resourceFileSystem.GetInfoAsync(candidateKey);
            if (infoResult.IsSuccess
                && infoResult.Value.Kind == StorageItemKind.NotFound)
            {
                defaultFolderName = candidateName;
                break;
            }
            folderNumber++;
        }

        return defaultFolderName;
    }

    /// <summary>
    /// Find a default file name that doesn't clash with an existing file on disk.
    /// Uses the previously saved file extension from settings.
    /// </summary>
    private async Task<string> FindDefaultFileNameAsync(ResourceKey parentFolderKey)
    {
        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        // Get the previously saved extension
        var extension = _workspaceWrapper.WorkspaceService.BindableWorkspaceSettings.PreviousNewFileExtension;

        string defaultFileName = string.Empty;
        int fileNumber = 1;
        while (true)
        {
            var candidateName = _stringLocalizer.GetString(DefaultFileNameKey, fileNumber).ToString();

            // Replace the default extension with the preferred extension
            candidateName = Path.ChangeExtension(candidateName, extension);

            var candidateKey = parentFolderKey.Combine(candidateName);
            var infoResult = await resourceFileSystem.GetInfoAsync(candidateKey);
            if (infoResult.IsSuccess
                && infoResult.Value.Kind == StorageItemKind.NotFound)
            {
                defaultFileName = candidateName;
                break;
            }
            fileNumber++;
        }

        return defaultFileName;
    }

    public static void NewFileDialog(ResourceKey parentFolderResource)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<ICreateResourceDialogCommand>(command =>
        {
            command.ResourceType = ResourceType.File;
            command.DestFolderResource = parentFolderResource;
        });
    }

    public static void NewFolderDialog(ResourceKey parentFolderResource)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<ICreateResourceDialogCommand>(command =>
        {
            command.ResourceType = ResourceType.Folder;
            command.DestFolderResource = parentFolderResource;
        });
    }
}
