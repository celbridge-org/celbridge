using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Commands;

public class ExtractArchiveDialogCommand : CommandBase, IExtractArchiveDialogCommand
{
    public override CommandFlags CommandFlags => CommandFlags.None;

    public ResourceKey ArchiveResource { get; set; }

    private readonly ILogger<ExtractArchiveDialogCommand> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IDialogService _dialogService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ExtractArchiveDialogCommand(
        ILogger<ExtractArchiveDialogCommand> logger,
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
        return await ShowExtractArchiveDialogAsync();
    }

    private async Task<Result> ShowExtractArchiveDialogAsync()
    {
        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return Result.Fail("Failed to show extract archive dialog because workspace is not loaded");
        }

        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        var getResult = await resourceFileSystem.GetResourceItemAsync(ArchiveResource);
        if (getResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve archive resource: '{ArchiveResource}'")
                .WithErrors(getResult);
        }

        var archiveName = ArchiveResource.ResourceName;
        var defaultFolderName = ArchiveResource.ResourceNameNoExtension;

        // Select the full default folder name
        var selectedRange = 0..defaultFolderName.Length;

        var titleString = _stringLocalizer.GetString("ResourceTree_ExtractArchiveTitle", archiveName);
        var messageString = _stringLocalizer.GetString("ResourceTree_EnterExtractFolderName");

        var validator = _serviceProvider.GetRequiredService<IResourceNameValidator>();
        var setParentResult = await validator.SetParentFolderAsync(ArchiveResource.GetParent());
        if (setParentResult.IsFailure)
        {
            return Result.Fail(setParentResult);
        }

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

            _commandService.Execute<IExtractArchiveCommand>(command =>
            {
                command.ArchiveResource = ArchiveResource;
                command.DestinationResource = destinationResource;
            });
        }

        return Result.Ok();
    }
}
