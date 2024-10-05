using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.FilePicker;
using Celbridge.Projects;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Utility methods for opening and closing projects on the main menu
/// </summary>
public class MainMenuUtils
{
    private readonly IDialogService _dialogService;
    private IFilePickerService _filePickerService;
    private readonly ICommandService _commandService;

    public MainMenuUtils(IDialogService dialogService,
                         IFilePickerService filePickerService,
                         ICommandService commandService)
    {
        _dialogService = dialogService;
        _filePickerService = filePickerService;
        _commandService = commandService;
    }

    public async Task ShowNewProjectDialogAsync()
    {
        var showResult = await _dialogService.ShowNewProjectDialogAsync();
        if (showResult.IsSuccess)
        {
            var projectConfig = showResult.Value;

            _commandService.Execute<ICreateProjectCommand>((command) =>
            {
                command.Config = projectConfig;
            });
        }
    }

    public async Task ShowOpenProjectDialogAsync()
    {
        var result = await _filePickerService.PickSingleFileAsync(new List<string> { FileExtensions.CelbridgeProject });
        if (result.IsSuccess)
        {
            var projectFilePath = result.Value;

            _commandService.Execute<ILoadProjectCommand>((command) =>
            {
                command.ProjectFilePath = projectFilePath;
            });
        }
    }
}
