using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Validators;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Commands;

public class AddResourceDialogCommand : CommandBase, IAddResourceDialogCommand
{
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    public ResourceType ResourceType { get; set; }
    public ResourceFormat ResourceFormat { get; set; }
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
        return await ShowAddResourceDialogAsync();
    }

    private async Task<Result> ShowAddResourceDialogAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to show add resource dialog because workspace is not loaded");
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;

        var getResult = resourceRegistry.GetResource(DestFolderResource);
        if (getResult.IsFailure)
        {
            return Result.Fail(getResult.Error);
        }

        var parentFolder = getResult.Value as IFolderResource;
        if (parentFolder is null)
        {
            return Result.Fail($"Parent folder resource key '{DestFolderResource}' does not reference a folder resource.");
        }

        var defaultStringKey = ResourceType == ResourceType.File ? "ResourceTree_DefaultFileName" : "ResourceTree_DefaultFolderName";
        var getDefaultResult = FindDefaultResourceName(defaultStringKey, parentFolder);
        if (getDefaultResult.IsFailure)
        {
            return Result.Fail()
                .WithErrors(getDefaultResult);
        }
        var defaultText = getDefaultResult.Value;

        var validator = _serviceProvider.GetRequiredService<IResourceNameValidator>();
        validator.ParentFolder = parentFolder;

        var titleStringKey = ResourceType == ResourceType.File ? "ResourceTree_AddFile" : "ResourceTree_AddFolder";
        var titleString = _stringLocalizer.GetString(titleStringKey);

        var enterNameString = _stringLocalizer.GetString("ResourceTree_EnterName");

        // Select only the filename part without the extension
        var extensionIndex = defaultText.LastIndexOf('.');
        var selectionRange = extensionIndex > 0 ? 0..extensionIndex : ..;

        var showResult = await _dialogService.ShowInputTextDialogAsync(
            titleString,
            enterNameString,
            defaultText,
            selectionRange,
            validator);

        if (showResult.IsSuccess)
        {
            var inputText = showResult.Value;

            var newResource = DestFolderResource.Combine(inputText);

            // Execute a command to add the resource
            _commandService.Execute<IAddResourceCommand>(command =>
            {
                command.ResourceType = ResourceType;
                command.DestResource = newResource;
                command.OpenAfterAdding = true;
            });
        }

        return Result.Ok();
    }

    /// <summary>
    /// Find a localized default resource name that doesn't clash with an existing resource on disk. 
    /// </summary>
    private Result<string> FindDefaultResourceName(string stringKey, IFolderResource? parentFolder)
    {
        if (parentFolder is null)
        {
            return Result<string>.Fail("Parent folder is null");
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;

        string defaultResourceName = string.Empty;
        int resourceNumber = 1;
        while (true)
        {
            var parentFolderPath = resourceRegistry.GetResourcePath(parentFolder);
            var candidateName = _stringLocalizer.GetString(stringKey, resourceNumber).ToString();

            // Default to the appropriate file extension for the specified file format.
            var extension = Path.GetExtension(candidateName);
            if (!string.IsNullOrEmpty(extension))
            {
                var newExtension = ResourceFormat switch
                {
                    ResourceFormat.Folder => string.Empty,
                    ResourceFormat.Excel => ExplorerConstants.ExcelExtension,
                    ResourceFormat.Markdown => ExplorerConstants.MarkdownExtension,
                    ResourceFormat.Python => ExplorerConstants.PythonExtension,
                    ResourceFormat.IPython => ExplorerConstants.IPythonExtension,
                    ResourceFormat.WebApp => ExplorerConstants.WebAppExtension,
                    _ => ExplorerConstants.TextExtension,
                };

                if (newExtension != extension)
                {
                    candidateName = Path.ChangeExtension(candidateName, newExtension);
                }
            }

            var candidatePath = Path.Combine(parentFolderPath, candidateName);
            if (!Directory.Exists(candidatePath) &&
                !File.Exists(candidatePath))
            {
                defaultResourceName = candidateName;
                break;
            }
            resourceNumber++;
        }

        return Result<string>.Ok(defaultResourceName);
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
