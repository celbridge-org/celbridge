using Celbridge.Messaging;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using Uno.Extensions;

namespace Celbridge.Console.ViewModels;

public partial class ConsolePanelViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly IDispatcher _dispatcher;

    private record LogEntry(string Level, string Message, LogEntryException? Exception);
    private record LogEntryException(string Type, string Message, string StackTrace);

    [ObservableProperty]
    private bool _isErrorBannerVisible;

    [ObservableProperty]
    private string _errorBannerTitle = string.Empty;

    [ObservableProperty]
    private string _errorBannerMessage = string.Empty;

    public ConsolePanelViewModel(
        IServiceProvider serviceProvider,
        IMessengerService messengerService,
        IDispatcher dispatcher,
        IWorkspaceWrapper workspaceWrapper)
    {
        _messengerService = messengerService;
        _dispatcher = dispatcher;

        // Register for console initialization error messages
        _messengerService.Register<ConsoleErrorMessage>(this, OnConsoleError);
    }

    private void OnConsoleError(object recipient, ConsoleErrorMessage message)
    {
        // Dispatch to UI thread since this may be called from a background thread
        _dispatcher.TryEnqueue(() =>
        {
            HandleConsoleError(message);
        });
    }

    private void HandleConsoleError(ConsoleErrorMessage message)
    {
        // Set the error banner properties based on error type
        switch (message.ErrorType)
        {
            case ConsoleErrorType.InvalidProjectConfig:
                ErrorBannerTitle = "Configuration Error";
                var configFile = message.ConfigFileName ?? "project configuration file";
                ErrorBannerMessage = $"There was an error parsing '{configFile}'. Please check the file for syntax errors.";
                break;

            case ConsoleErrorType.PythonPreInitError:
                ErrorBannerTitle = "Python Initialization Error";
                ErrorBannerMessage = "Failed to initialize Python. Please check the console output for more details and verify your Python configuration.";
                break;

            case ConsoleErrorType.PythonProcessExited:
                ErrorBannerTitle = "Console Process Exited";
                ErrorBannerMessage = "The console process has exited unexpectedly. Please reload the project to restart the console.";
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }

        IsErrorBannerVisible = true;
    }

    public void Cleanup()
    {
        _messengerService.UnregisterAll(this);
    }
}
