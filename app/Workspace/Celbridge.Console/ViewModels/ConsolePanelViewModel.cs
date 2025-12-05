using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;

using Path = System.IO.Path;

namespace Celbridge.Console.ViewModels;

public partial class ConsolePanelViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly IDispatcher _dispatcher;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IProjectService _projectService;

    private record LogEntry(string Level, string Message, LogEntryException? Exception);
    private record LogEntryException(string Type, string Message, string StackTrace);

    [ObservableProperty]
    private bool _isErrorBannerVisible;

    [ObservableProperty]
    private string _errorBannerTitle = string.Empty;

    [ObservableProperty]
    private string _errorBannerMessage = string.Empty;

    [ObservableProperty]
    private bool _isReloadButtonVisible;

    public ConsolePanelViewModel(
        IServiceProvider serviceProvider,
        IMessengerService messengerService,
        IDispatcher dispatcher,
        IStringLocalizer stringLocalizer,
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _messengerService = messengerService;
        _dispatcher = dispatcher;
        _stringLocalizer = stringLocalizer;
        _projectService = projectService;

        // Register for console initialization error messages
        _messengerService.Register<ConsoleErrorMessage>(this, OnConsoleError);
    }

    public void OnTerminalProcessExited()
    {
        var projectFilePath = _projectService?.CurrentProject?.ProjectFilePath;
        Guard.IsNotNull(projectFilePath);
        var projectFile = Path.GetFileName(projectFilePath);

        // Broadcast a console error message.
        // This message will be handled by OnConsoleError() in this class.
        var errorMessage = new ConsoleErrorMessage(ConsoleErrorType.PythonProcessError, projectFile);
        _messengerService.Send(errorMessage);
    }

    private void OnConsoleError(object recipient, ConsoleErrorMessage message)
    {
        // This handler may be called from a background thread so ensure that the message
        // is handled on the main UI thread.
        _dispatcher.TryEnqueue(() =>
        {
            HandleConsoleError(message);
        });
    }

    private void HandleConsoleError(ConsoleErrorMessage message)
    {
        var configFile = message.ConfigFileName ?? "project configuration file";

        // Set the error banner properties based on error type
        switch (message.ErrorType)
        {
            case ConsoleErrorType.InvalidProjectConfig:
                ErrorBannerTitle = _stringLocalizer.GetString("ConsolePanel_ProjectConfigErrorTitle");
                ErrorBannerMessage = _stringLocalizer.GetString("ConsolePanel_ProjectConfigErrorMessage", configFile);
                break;

            case ConsoleErrorType.PythonPreInitError:
                ErrorBannerTitle = _stringLocalizer.GetString("ConsolePanel_PythonInitializationErrorTitle");
                ErrorBannerMessage = _stringLocalizer.GetString("ConsolePanel_PythonInitializationErrorMessage", configFile);
                break;

            case ConsoleErrorType.PythonProcessError:
                ErrorBannerTitle = _stringLocalizer.GetString("ConsolePanel_PythonProcessErrorTitle");
                ErrorBannerMessage = _stringLocalizer.GetString("ConsolePanel_PythonProcessErrorMessage", configFile);
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }

        IsErrorBannerVisible = true;
        IsReloadButtonVisible = true;
    }

    public void OnReloadProjectClicked()
    {
        // Send message to request project reload
        _messengerService.Send<ReloadProjectMessage>();
    }

    public void Cleanup()
    {
        _messengerService.UnregisterAll(this);
    }
}
