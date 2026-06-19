using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Commands;

public class AddResourceDialogCommand : CommandBase, IAddResourceDialogCommand
{
    private const string AddFolderTitleKey = "ResourceTree_AddFolder";
    private const string FolderNameKey = "AddFolderDialog_FolderName";
    private const string DefaultFolderNameKey = "ResourceTree_DefaultFolderName";
    private const string DefaultFileNameKey = "ResourceTree_DefaultFileName";
    private const string AddButtonKey = "DialogButton_Add";

    public override CommandFlags CommandFlags => CommandFlags.None;

    public ResourceType ResourceType { get; set; }
    public ResourceKey DestFolderResource { get; set; }

    private readonly IServiceProvider _serviceProvider;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IDialogService _dialogService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public AddResourceDialogCommand(
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
        if (ResourceType == ResourceType.File)
        {
            return await ShowAddFileDialogAsync();
        }
        else
        {
            return await ShowAddFolderDialogAsync();
        }
    }

    private async Task<Result> ShowAddFileDialogAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to show add file dialog because workspace is not loaded");
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var getResult = resourceRegistry.GetResource(DestFolderResource);
        if (getResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve destination folder: '{DestFolderResource}'")
                .WithErrors(getResult);
        }

        var parentFolder = getResult.Value as IFolderResource;
        if (parentFolder is null)
        {
            return Result.Fail($"Parent folder resource key '{DestFolderResource}' does not reference a folder resource.");
        }

        var getDefaultResult = await FindDefaultFileNameAsync(parentFolder);
        if (getDefaultResult.IsFailure)
        {
            return Result.Fail()
                .WithErrors(getDefaultResult);
        }
        var defaultFileName = getDefaultResult.Value;

        var validator = _serviceProvider.GetRequiredService<IResourceNameValidator>();
        validator.ParentFolder = parentFolder;
        validator.ValidateAsFolder = false;

        // Select only the filename part without the extension
        var extensionIndex = defaultFileName.LastIndexOf('.');
        var selectionRange = extensionIndex > 0 ? 0..extensionIndex : ..;

        var showResult = await _dialogService.ShowAddFileDialogAsync(
            defaultFileName,
            selectionRange,
            validator);

        if (showResult.IsSuccess)
        {
            var config = showResult.Value;
            var newResource = DestFolderResource.Combine(config.FileName);

            // Execute a command to add the resource
            _commandService.Execute<IAddResourceCommand>(command =>
            {
                command.ResourceType = ResourceType.File;
                command.DestResource = newResource;
                command.OpenAfterAdding = true;
            });
        }

        return Result.Ok();
    }

    private async Task<Result> ShowAddFolderDialogAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to show add folder dialog because workspace is not loaded");
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var getResult = resourceRegistry.GetResource(DestFolderResource);
        if (getResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve destination folder: '{DestFolderResource}'")
                .WithErrors(getResult);
        }

        var parentFolder = getResult.Value as IFolderResource;
        if (parentFolder is null)
        {
            return Result.Fail($"Parent folder resource key '{DestFolderResource}' does not reference a folder resource.");
        }

        var getDefaultResult = await FindDefaultFolderNameAsync(parentFolder);
        if (getDefaultResult.IsFailure)
        {
            return Result.Fail()
                .WithErrors(getDefaultResult);
        }
        var defaultText = getDefaultResult.Value;

        var validator = _serviceProvider.GetRequiredService<IResourceNameValidator>();
        validator.ParentFolder = parentFolder;
        validator.ValidateAsFolder = true;

        var titleString = _stringLocalizer.GetString(AddFolderTitleKey);
        var nameString = _stringLocalizer.GetString(FolderNameKey);

        // Select the entire folder name
        var selectionRange = ..;

        var showResult = await _dialogService.ShowInputTextDialogAsync(
            titleString,
            nameString,
            defaultText,
            selectionRange,
            validator,
            AddButtonKey);

        if (showResult.IsSuccess)
        {
            var inputText = showResult.Value;

            var newResource = DestFolderResource.Combine(inputText);

            // Execute a command to add the resource
            _commandService.Execute<IAddResourceCommand>(command =>
            {
                command.ResourceType = ResourceType.Folder;
                command.DestResource = newResource;
                command.OpenAfterAdding = false;
            });
        }

        return Result.Ok();
    }

    /// <summary>
    /// Find a default folder name that doesn't clash with an existing folder on disk.
    /// </summary>
    private async Task<Result<string>> FindDefaultFolderNameAsync(IFolderResource? parentFolder)
    {
        if (parentFolder is null)
        {
            return Result<string>.Fail("Parent folder is null");
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
        var parentFolderKey = resourceRegistry.GetResourceKey(parentFolder);

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

        return Result<string>.Ok(defaultFolderName);
    }

    /// <summary>
    /// Find a default file name that doesn't clash with an existing file on disk.
    /// Uses the previously saved file extension from settings.
    /// </summary>
    private async Task<Result<string>> FindDefaultFileNameAsync(IFolderResource? parentFolder)
    {
        if (parentFolder is null)
        {
            return Result<string>.Fail("Parent folder is null");
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        // Get the previously saved extension
        var extension = _workspaceWrapper.WorkspaceService.Settings.PreviousNewFileExtension;

        var parentFolderKey = resourceRegistry.GetResourceKey(parentFolder);

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

        return Result<string>.Ok(defaultFileName);
    }

    //
    // Static methods for scripting support.
    //

    public static void AddFileDialog(ResourceKey parentFolderResource)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IAddResourceDialogCommand>(command =>
        {
            command.ResourceType = ResourceType.File;
            command.DestFolderResource = parentFolderResource;
        });
    }

    public static void AddFolderDialog(ResourceKey parentFolderResource)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IAddResourceDialogCommand>(command =>
        {
            command.ResourceType = ResourceType.Folder;
            command.DestFolderResource = parentFolderResource;
        });
    }
}
