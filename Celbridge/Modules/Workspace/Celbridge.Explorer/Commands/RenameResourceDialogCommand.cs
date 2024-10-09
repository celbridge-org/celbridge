using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Dialog;
using Celbridge.Foundation;
using Celbridge.Validators;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Commands;

public class RenameResourceDialogCommand : CommandBase, IRenameResourceDialogCommand
{
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    public ResourceKey Resource { get; set; }

    private readonly IServiceProvider _serviceProvider;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IDialogService _dialogService;

    public RenameResourceDialogCommand(
        IServiceProvider serviceProvider,
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        IDialogService dialogService)
    {
        _serviceProvider = serviceProvider;
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

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;

        var getResult = resourceRegistry.GetResource(Resource);
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

        var enterNewNameString = _stringLocalizer.GetString("ResourceTree_EnterNewName");

        var showResult = await _dialogService.ShowInputTextDialogAsync(
            renameResourceString,
            enterNewNameString,
            defaultText,
            selectedRange,
            validator);

        if (showResult.IsSuccess)
        {
            var inputText = showResult.Value;

            var sourceParentResource = Resource.GetParent();
            var destResource = sourceParentResource.Combine(inputText);

            bool isFolderResource = resource is IFolderResource;

            // Maintain the expanded state of folders after rename
            bool isExpandedFolder = isFolderResource &&
                resourceRegistry.IsFolderExpanded(Resource);

            // Execute a command to move the resource to perform the rename
            _commandService.Execute<ICopyResourceCommand>(command =>
            {
                command.SourceResource = Resource;
                command.DestResource = destResource;
                command.TransferMode = DataTransferMode.Move;

                if (isExpandedFolder)
                {
                    command.ExpandCopiedFolder = true;
                }
            });
        }

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void RenameResourceDialog(ResourceKey resource)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IRenameResourceDialogCommand>(command =>
        {
            command.Resource = resource;
        });
    }
}
