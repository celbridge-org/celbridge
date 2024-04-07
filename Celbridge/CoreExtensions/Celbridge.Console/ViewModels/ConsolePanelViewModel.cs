using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.UserInterface;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Console.ViewModels;

public partial class ConsolePanelViewModel : ObservableObject
{
    private readonly ILoggingService _loggingService;
    private readonly IConsoleService _consoleService;

    [ObservableProperty]
    private string _inputText = string.Empty;

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

    public ICommand SubmitCommand => new RelayCommand(Submit_Executed);
    private void Submit_Executed()
    {
        _loggingService.Info(InputText);

        // Todo: Add this command to the command history
        // Todo: Maintain separate console contexts for CelScript, C#, Python, ChatGPT, etc.
        // Todo: Select the active context from a tab view in the console window
        // Todo: User can add a new console context tab via + button
        // Todo: Register a new console context via an extension

        InputText = string.Empty;
    }
}
