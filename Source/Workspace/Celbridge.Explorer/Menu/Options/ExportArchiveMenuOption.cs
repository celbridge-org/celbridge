using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to export the project folder as a zip archive, to a file the user picks in the save dialog.
/// </summary>
public class ExportArchiveMenuOption : IMenuOption<ExplorerMenuContext>
{
    private const string ZipFileExtension = ".zip";

    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IFilePickerService _filePickerService;
    private readonly ILogger<ExportArchiveMenuOption> _logger;

    public int Priority => 6;
    public string GroupId => nameof(ExplorerMenuGroup.EditActions);

    public ExportArchiveMenuOption(
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        IFilePickerService filePickerService,
        ILogger<ExportArchiveMenuOption> logger)
    {
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _filePickerService = filePickerService;
        _logger = logger;
    }

    public MenuItemDisplayInfo GetDisplayInfo(ExplorerMenuContext context)
    {
        return new MenuItemDisplayInfo(
            _stringLocalizer.GetString("ResourceTree_ExportArchive"),
            Icon: IconSymbol.Archive);
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        return new MenuItemState(
            IsVisible: context.IsProjectFolderTargeted,
            IsEnabled: context.IsProjectFolderTargeted);
    }

    public async void Execute(ExplorerMenuContext context)
    {
        try
        {
            await ExecuteAsync(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export Archive menu option failed");
        }
    }

    // The save dialog is shown before the command is queued, so the command queue keeps running while the
    // user chooses where to save.
    private async Task ExecuteAsync(ExplorerMenuContext context)
    {
        if (!context.IsProjectFolderTargeted)
        {
            return;
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        // The project folder resource has an empty name, so the archive is named after the folder on disk.
        var projectFolderName = Path.GetFileName(resourceRegistry.ProjectFolderPath);
        var suggestedFileName = $"{projectFolderName}{ZipFileExtension}";
        var fileTypeDescription = _stringLocalizer.GetString("ResourceTree_ExportArchiveFileType");
        var fileExtensions = new List<string>
        {
            ZipFileExtension
        };

        var pickResult = await _filePickerService.PickSaveFileAsync(suggestedFileName, fileTypeDescription, fileExtensions);
        if (pickResult.IsFailure)
        {
            return;
        }
        var archiveFilePath = pickResult.Value;

        var projectFolderKey = resourceRegistry.GetResourceKey(context.ProjectFolder);

        _commandService.Execute<IExportArchiveCommand>(command =>
        {
            command.SourceResource = projectFolderKey;
            command.ArchiveFilePath = archiveFilePath;
        });
    }
}
