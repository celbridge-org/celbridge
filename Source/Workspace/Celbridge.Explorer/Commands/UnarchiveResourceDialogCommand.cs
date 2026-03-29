using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Commands;

public class UnarchiveResourceDialogCommand : CommandBase, IUnarchiveResourceDialogCommand
{
    public override CommandFlags CommandFlags => CommandFlags.None;

    public ResourceKey ArchiveResource { get; set; }

    private readonly ILogger<UnarchiveResourceDialogCommand> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IDialogService _dialogService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public UnarchiveResourceDialogCommand(
        ILogger<UnarchiveResourceDialogCommand> logger,
        IServiceProvider serviceProvider,
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IDialogService dialogService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
        _dialogService = dialogService;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        return await ShowUnarchiveResourceDialogAsync();
    }

    private async Task<Result> ShowUnarchiveResourceDialogAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Failed to show extract archive dialog because workspace is not loaded");
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var getResult = resourceRegistry.GetResource(ArchiveResource);
        if (getResult.IsFailure)
        {
            return Result.Fail(getResult.Error);
        }
        var resource = getResult.Value;

        var archiveName = resource.Name;
        var defaultFolderName = ArchiveResource.ResourceNameNoExtension;

        // Select the full default folder name
        var selectedRange = 0..defaultFolderName.Length;

        var titleString = _stringLocalizer.GetString("ResourceTree_ExtractArchiveTitle", archiveName);
        var messageString = _stringLocalizer.GetString("ResourceTree_EnterExtractFolderName");

        var validator = _serviceProvider.GetRequiredService<IResourceNameValidator>();
        validator.ParentFolder = resource.ParentFolder;

        var showResult = await _dialogService.ShowInputTextDialogAsync(
            titleString,
            messageString,
            defaultFolderName,
            selectedRange,
            validator);

        if (showResult.IsSuccess)
        {
            var folderName = showResult.Value;

            var parentResource = ArchiveResource.GetParent();
            var destinationResource = parentResource.Combine(folderName);

            _commandService.Execute<IUnarchiveResourceCommand>(command =>
            {
                command.ArchiveResource = ArchiveResource;
                command.DestinationResource = destinationResource;
            });
        }

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void UnarchiveResourceDialog(ResourceKey archiveResource)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IUnarchiveResourceDialogCommand>(command =>
        {
            command.ArchiveResource = archiveResource;
        });
    }
}
