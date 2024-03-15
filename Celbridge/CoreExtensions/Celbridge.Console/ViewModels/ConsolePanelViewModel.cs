using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.UserInterface;

namespace Celbridge.Console.ViewModels;

public class ConsolePanelViewModel
{
    private readonly ILoggingService _loggingService;
    private readonly IConsoleService _consoleService;

    public ConsolePanelViewModel(
        IUserInterfaceService userInterfaceService, 
        ILoggingService loggingService,
        IConsoleService consoleService)
    {
        _loggingService = loggingService;
        _consoleService = consoleService;  // Transient instance created via DI

        // Register the console service with the workspace service
        userInterfaceService.WorkspaceService.RegisterService(_consoleService);
    }

    public ICommand ClearCommand => new RelayCommand(Clear_Executed);
    private void Clear_Executed()
    {
        _loggingService.Info("Clear");
    }
}
