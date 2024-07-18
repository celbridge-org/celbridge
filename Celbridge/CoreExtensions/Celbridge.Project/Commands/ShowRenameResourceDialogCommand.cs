using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Validators;
using Celbridge.BaseLibrary.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Project.Commands;

public class ShowRenameResourceDialogCommand : CommandBase, IShowRenameResourceDialogCommand
{
    public override string UndoStackName => UndoStackNames.None;

    public ResourceKey ResourceKey { get; set; }

    private readonly IServiceProvider _serviceProvider;
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IDialogService _dialogService;

    public ShowRenameResourceDialogCommand(
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
        return await ShowRenameResourceDialogAsync();
    }

    private async Task<Result> ShowRenameResourceDialogAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to show add resource dialog because workspace is not loaded");
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ProjectService.ResourceRegistry;

        var getResult = resourceRegistry.GetResource(ResourceKey);
        if (getResult.IsFailure)
        {
            return Result.Fail(getResult.Error);
        }
        var resource = getResult.Value;

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

        var renameResourceString = _stringLocalizer.GetString("ResourceTree_RenameResource", resourceName);

        var defaultText = resourceName;

        var validator = _serviceProvider.GetRequiredService<IResourceNameValidator>();
        validator.ParentFolder = resource.ParentFolder;
        validator.ValidNames.Add(resourceName); // The original name is always valid when renaming

        var EnterNameString = _stringLocalizer.GetString("ResourceTree_EnterName");
        var EnterNewNameString = _stringLocalizer.GetString("ResourceTree_EnterNewName");

        var showResult = await _dialogService.ShowInputTextDialogAsync(
            renameResourceString,
            EnterNewNameString,
            defaultText,
            selectedRange,
            validator);

        if (showResult.IsSuccess)
        {
            var inputText = showResult.Value;

            var sourceResourceKey = ResourceKey;
            var parentResourceKey = ResourceKey.GetParent();
            var destResourceKey = parentResourceKey.IsEmpty ? (ResourceKey)inputText : parentResourceKey.Combine(inputText);

            bool isFolderResource = resource is IFolderResource;

            // Maintain the expanded state of folders after rename
            bool isExpandedFolder = isFolderResource &&
                resourceRegistry.IsFolderExpanded(ResourceKey);

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

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void ShowRenameResourceDialog(ResourceKey resourceKey)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IShowRenameResourceDialogCommand>(command =>
        {
            command.ResourceKey = resourceKey;
        });
    }
}
