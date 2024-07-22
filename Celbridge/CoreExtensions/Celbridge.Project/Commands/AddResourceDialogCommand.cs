using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Validators;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Projects.Commands;

public class AddResourceDialogCommand : CommandBase, IAddResourceDialogCommand
{
    public override string UndoStackName => UndoStackNames.None;

    public ResourceType ResourceType { get; set; }
    public ResourceKey DestFolderResource { get; set; }

    private readonly IServiceProvider _serviceProvider;
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IDialogService _dialogService;

    public AddResourceDialogCommand(
        IServiceProvider serviceProvider,
        IMessengerService messengerService,
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        IDialogService dialogService)
    {
        _serviceProvider = serviceProvider;
        _messengerService = messengerService;
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _dialogService = dialogService;
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

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ProjectService.ResourceRegistry;

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
        var defaultText = FindDefaultResourceName(defaultStringKey, parentFolder);

        var validator = _serviceProvider.GetRequiredService<IResourceNameValidator>();
        validator.ParentFolder = parentFolder;

        var titleStringKey = ResourceType == ResourceType.File ? "ResourceTree_AddFile" : "ResourceTree_AddFolder";
        var titleString = _stringLocalizer.GetString(titleStringKey);

        var enterNameString = _stringLocalizer.GetString("ResourceTree_EnterName");

        var showResult = await _dialogService.ShowInputTextDialogAsync(
            titleString,
            enterNameString,
            defaultText,
            ..,
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
            });

            var message = new RequestResourceTreeUpdateMessage();
            _messengerService.Send(message);
        }

        return Result.Ok();
    }

    /// <summary>
    /// Find a localized default resource name that doesn't clash with an existing resource on disk. 
    /// </summary>
    private string FindDefaultResourceName(string stringKey, IFolderResource? parentFolder)
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ProjectService.ResourceRegistry;

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

    //
    // Static methods for scripting support.
    //

    public static void AddFileDialog(ResourceKey parentFolderResource)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IAddResourceDialogCommand>(command =>
        {
            command.ResourceType = ResourceType.File;
            command.DestFolderResource = parentFolderResource;
        });
    }

    public static void AddFolderDialog(ResourceKey parentFolderResource)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IAddResourceDialogCommand>(command =>
        {
            command.ResourceType = ResourceType.Folder;
            command.DestFolderResource = parentFolderResource;
        });
    }
}
