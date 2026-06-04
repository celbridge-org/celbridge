using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Dialog;
using Celbridge.Utilities;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Commands;

public class DuplicateResourceDialogCommand : CommandBase, IDuplicateResourceDialogCommand
{
    public override CommandFlags CommandFlags => CommandFlags.None;

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

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var getResult = resourceRegistry.GetResource(Resource);
        if (getResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve resource: '{Resource}'")
                .WithErrors(getResult);
        }
        var resource = getResult.Value;

        // Pre-populate the dialog with the auto-generated name the silent
        // duplicate path would have chosen (e.g. "foo - Copy.md"). Matches
        // Windows Explorer / macOS Finder behaviour and saves keystrokes in
        // the common case; the user can still clear and type something else.
        // If the helper somehow can't produce a unique name (very rare; would
        // mean 1000+ existing copies of this name) we fall back to the
        // original name and let the validator reject it on dialog submit.
        var defaultKeyResult = ResourceNameHelper.GenerateUniqueDuplicateKey(Resource, resourceRegistry);
        var defaultText = defaultKeyResult.IsSuccess
            ? defaultKeyResult.Value.ResourceName
            : resource.Name;

        // Select only the filename part without the extension so the user can
        // type a replacement basename immediately.
        var extensionIndex = defaultText.LastIndexOf('.');
        var selectedRange = extensionIndex > 0 ? 0..extensionIndex : ..;

        var duplicateResourceString = _stringLocalizer.GetString("ResourceTree_DuplicateResource", resource.Name);
        var enterNameString = _stringLocalizer.GetString("ResourceTree_DuplicateResourceEnterName");

        var validator = _serviceProvider.GetRequiredService<IResourceNameValidator>();
        validator.ParentFolder = resource.ParentFolder;
        validator.ValidateAsFolder = resource is IFolderResource;

        var showResult = await _dialogService.ShowInputTextDialogAsync(
            duplicateResourceString,
            enterNameString,
            defaultText,
            selectedRange,
            validator);

        if (showResult.IsSuccess)
        {
            var inputText = showResult.Value;
            var destResource = Resource.GetParent().Combine(inputText);

            // Preserve folder-expansion state across the copy so a duplicated
            // expanded folder lands expanded in the tree.
            bool isExpandedFolder = false;
            if (resource is IFolderResource)
            {
                var folderStateService = _workspaceWrapper.WorkspaceService.ExplorerService.FolderStateService;
                isExpandedFolder = folderStateService.IsExpanded(Resource);
            }

            // Issue the copy as a top-level command rather than wrapping it in
            // another command that would await it from inside the executor. The
            // command queue is single-threaded; a command's body awaiting
            // another command via ExecuteAsync deadlocks the queue.
            _commandService.Execute<ICopyResourceCommand>(command =>
            {
                command.SourceResources = new List<ResourceKey> { Resource };
                command.DestResource = destResource;
                command.TransferMode = DataTransferMode.Copy;
                command.ExpandCopiedFolder = isExpandedFolder;
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
