using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Commands;

public class ArchiveResourceDialogCommand : CommandBase, IArchiveResourceDialogCommand
{
    public override CommandFlags CommandFlags => CommandFlags.None;

    public ResourceKey FolderResource { get; set; }

    private readonly ILogger<ArchiveResourceDialogCommand> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IDialogService _dialogService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ArchiveResourceDialogCommand(
        ILogger<ArchiveResourceDialogCommand> logger,
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
        return await ShowArchiveResourceDialogAsync();
    }

    private async Task<Result> ShowArchiveResourceDialogAsync()
    {
        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return Result.Fail("Failed to show create archive dialog because workspace is not loaded");
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var getResult = resourceRegistry.GetResource(FolderResource);
        if (getResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve folder resource: '{FolderResource}'")
                .WithErrors(getResult);
        }
        var resource = getResult.Value;

        // The archive is created beside the folder, in its parent folder. The project folder resource has
        // no parent and an empty name, so its archive is created inside it, named after the folder on disk.
        var folderName = resource.Name;
        var destinationFolder = resource.ParentFolder;
        if (destinationFolder is null)
        {
            folderName = Path.GetFileName(resourceRegistry.ProjectFolderPath);
            destinationFolder = resourceRegistry.ProjectFolder;
        }

        var defaultArchiveName = $"{folderName}.zip";

        // Select just the folder name part, before ".zip"
        var selectedRange = 0..folderName.Length;

        var titleString = _stringLocalizer.GetString("ResourceTree_CreateArchiveTitle", folderName);
        var messageString = _stringLocalizer.GetString("ResourceTree_EnterArchiveName");

        var validator = _serviceProvider.GetRequiredService<IResourceNameValidator>();
        validator.ParentFolder = destinationFolder;

        var showResult = await _dialogService.ShowInputTextDialogAsync(
            titleString,
            messageString,
            defaultArchiveName,
            selectedRange,
            validator);

        if (showResult.IsSuccess)
        {
            var archiveName = showResult.Value;

            var destinationFolderKey = resourceRegistry.GetResourceKey(destinationFolder);
            var archiveResource = destinationFolderKey.Combine(archiveName);

            _commandService.Execute<IArchiveResourceCommand>(command =>
            {
                command.SourceResource = FolderResource;
                command.ArchiveResource = archiveResource;
            });
        }

        return Result.Ok();
    }
}
