using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Validators;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Commands;

public class DuplicateResourceDialogCommand : CommandBase, IDuplicateResourceDialogCommand
{
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    public ResourceKey Resource { get; set; }

    private readonly IServiceProvider _serviceProvider;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IDialogService _dialogService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public DuplicateResourceDialogCommand(
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
        return await ShowDuplicateResourceDialogAsync();
    }

    private async Task<Result> ShowDuplicateResourceDialogAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to show duplicate resource dialog because workspace is not loaded");
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

        var duplicateResourceString = _stringLocalizer.GetString("ResourceTree_DuplicateResource", resourceName);

        var defaultText = resourceName;

        var validator = _serviceProvider.GetRequiredService<IResourceNameValidator>();
        validator.ParentFolder = resource.ParentFolder;
        validator.ValidNames.Add(resourceName); // The original name is always valid when renaming

        var enterNameString = _stringLocalizer.GetString("ResourceTree_DuplicateResourceEnterName");

        var showResult = await _dialogService.ShowInputTextDialogAsync(
            duplicateResourceString,
            enterNameString,
            defaultText,
            selectedRange,
            validator);

        if (showResult.IsSuccess)
        {
            var inputText = showResult.Value;

            var sourceParentResource = Resource.GetParent();
            var destResource = sourceParentResource.Combine(inputText);

            if (Resource == destResource)
            {
                // Choosing the original name is treated as a cancel.
                return Result.Ok();
            }

            bool isFolderResource = resource is IFolderResource;

            // Maintain the expanded state of folders after rename
            bool isExpandedFolder = isFolderResource &&
                resourceRegistry.IsFolderExpanded(Resource);

            // Execute a command to move the folder resource to perform the rename
            _commandService.Execute<ICopyResourceCommand>(command =>
            {
                command.SourceResource = Resource;
                command.DestResource = destResource;

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

    public static void DuplicateResourceDialog(ResourceKey resource)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IDuplicateResourceDialogCommand>(command =>
        {
            command.Resource = resource;
        });
    }
}
