using Celbridge.Commands;
using Celbridge.Explorer;
using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;
using System.Security.Cryptography;

using Path = System.IO.Path;

namespace Celbridge.Console.ViewModels;

public partial class ConsolePanelViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly IDispatcher _dispatcher;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IProjectService _projectService;
    private readonly ICommandService _commandService;

    private record LogEntry(string Level, string Message, LogEntryException? Exception);
    private record LogEntryException(string Type, string Message, string StackTrace);

    [ObservableProperty]
    private bool _isErrorBannerVisible;

    [ObservableProperty]
    private string _errorBannerTitle = string.Empty;

    [ObservableProperty]
    private string _errorBannerMessage = string.Empty;

    [ObservableProperty]
    private string _projectChangeBannerTitle = string.Empty;
    
    [ObservableProperty]
    private string _projectChangeBannerMessage = string.Empty;

    [ObservableProperty]
    private bool _isProjectChangeBannerVisible;

    [ObservableProperty]
    private bool _isMigrationBannerVisible;

    [ObservableProperty]
    private string _migrationBannerTitle = string.Empty;

    [ObservableProperty]
    private string _migrationBannerMessage = string.Empty;

    private byte[]? _originalProjectFileHash = null;

    public ConsolePanelViewModel(
        IServiceProvider serviceProvider,
        IMessengerService messengerService,
        IDispatcher dispatcher,
        IStringLocalizer stringLocalizer,
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService)
    {
        _messengerService = messengerService;
        _dispatcher = dispatcher;
        _stringLocalizer = stringLocalizer;
        _projectService = projectService;
        _commandService = commandService;

        // Register for console initialization error messages
        _messengerService.Register<ConsoleErrorMessage>(this, OnConsoleError);

        // Register for resource change messages to monitor project file changes
        _messengerService.Register<MonitoredResourceChangedMessage>(this, OnMonitoredResourceChanged);

        // Store the original project file contents
        StoreProjectFileHash();
        
        // Check if the project was migrated and show banner if needed
        CheckMigrationStatus();
    }

    public void OnTerminalProcessExited()
    {
        var projectFilePath = _projectService?.CurrentProject?.ProjectFilePath;
        Guard.IsNotNull(projectFilePath);
        var projectFile = Path.GetFileName(projectFilePath);

        // Broadcast a console error message.
        // This message will be handled by OnConsoleError() in this class.
        var errorMessage = new ConsoleErrorMessage(ConsoleErrorType.PythonHostProcessError, projectFile);
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

            case ConsoleErrorType.PythonHostPreInitError:
                ErrorBannerTitle = _stringLocalizer.GetString("ConsolePanel_PythonInitializationErrorTitle");
                ErrorBannerMessage = _stringLocalizer.GetString("ConsolePanel_PythonInitializationErrorMessage", configFile);
                break;

            case ConsoleErrorType.PythonHostProcessError:
                ErrorBannerTitle = _stringLocalizer.GetString("ConsolePanel_PythonProcessErrorTitle");
                ErrorBannerMessage = _stringLocalizer.GetString("ConsolePanel_PythonProcessErrorMessage", configFile);
                break;

            case ConsoleErrorType.IncompatibleVersion:
                ErrorBannerTitle = _stringLocalizer.GetString("ConsolePanel_IncompatibleVersionTitle");
                ErrorBannerMessage = _stringLocalizer.GetString("ConsolePanel_IncompatibleVersionMessage", configFile);
                break;

            case ConsoleErrorType.InvalidVersion:
                ErrorBannerTitle = _stringLocalizer.GetString("ConsolePanel_InvalidVersionTitle");
                ErrorBannerMessage = _stringLocalizer.GetString("ConsolePanel_InvalidVersionMessage", configFile);
                break;

            case ConsoleErrorType.MigrationError:
                ErrorBannerTitle = _stringLocalizer.GetString("ConsolePanel_MigrationErrorTitle");
                ErrorBannerMessage = _stringLocalizer.GetString("ConsolePanel_MigrationErrorMessage", configFile);
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }

        IsErrorBannerVisible = true;
        ShowConsolePanel();

        // Hide project change banner when error banner is shown
        IsProjectChangeBannerVisible = false;
    }

    public void OnReloadProjectClicked()
    {
        // Send message to request project reload
        _messengerService.Send<ReloadProjectMessage>();
    }

    private void ShowConsolePanel()
    {
        // Force the console panel to be visible when an error occurs
        // This ensures the user can see the error banner even if they had previously collapsed the console
        _commandService.Execute<ISetPanelVisibilityCommand>(command =>
        {
            command.Panels = PanelVisibilityFlags.Console;
            command.IsVisible = true;
        });
    }

    private void OnMonitoredResourceChanged(object recipient, MonitoredResourceChangedMessage message)
    {
        // Check if the changed resource is the .celbridge project file
        var projectFilePath = _projectService?.CurrentProject?.ProjectFilePath;
        if (string.IsNullOrEmpty(projectFilePath))
        {
            return;
        }

        var projectFileName = Path.GetFileName(projectFilePath);
        var changedResourcePath = message.Resource.ToString();

        if (changedResourcePath.Equals(projectFileName, StringComparison.OrdinalIgnoreCase))
        {
            // This handler may be called from a background thread so ensure that the message
            // is handled on the main UI thread.
            _dispatcher.TryEnqueue(() =>
            {
                CheckProjectFileChanged();
            });
        }
    }

    private void StoreProjectFileHash()
    {
        var projectFilePath = _projectService?.CurrentProject?.ProjectFilePath;
        if (string.IsNullOrEmpty(projectFilePath) || !File.Exists(projectFilePath))
        {
            _originalProjectFileHash = null;
            return;
        }

        try
        {
            var fileContents = File.ReadAllText(projectFilePath);
            _originalProjectFileHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fileContents));
        }
        catch
        {
            _originalProjectFileHash = null;
        }
    }

    private void CheckProjectFileChanged()
    {
        var projectFilePath = _projectService?.CurrentProject?.ProjectFilePath;
        if (string.IsNullOrEmpty(projectFilePath) || !File.Exists(projectFilePath))
        {
            return;
        }

        try
        {
            var currentContents = File.ReadAllText(projectFilePath);
            var currentHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(currentContents));

            // If error banner is visible, don't show the project change banner
            if (IsErrorBannerVisible)
            {
                IsProjectChangeBannerVisible = false;
                return;
            }

            // Check if the hash has changed from the original
            if (_originalProjectFileHash == null || 
                !currentHash.SequenceEqual(_originalProjectFileHash))
            {
                // Populate the project change banner strings
                ProjectChangeBannerTitle = _stringLocalizer.GetString("ConsolePanel_ProjectChangeBannerTitle");
                ProjectChangeBannerMessage = _stringLocalizer.GetString("ConsolePanel_ProjectChangeBannerMessage");

                IsProjectChangeBannerVisible = true;
                ShowConsolePanel();
            }
            else
            {
                IsProjectChangeBannerVisible = false;
            }
        }
        catch
        {
            // If we can't read the file, hide the banner
            IsProjectChangeBannerVisible = false;
        }
    }

    public void OnProjectChangeBannerClosed()
    {
        IsProjectChangeBannerVisible = false;
    }

    private void CheckMigrationStatus()
    {
        var currentProject = _projectService?.CurrentProject;
        if (currentProject == null)
        {
            return;
        }

        // Only show the migration banner if there was an actual version change
        var oldVersion = currentProject.MigrationResult.OldVersion;
        var newVersion = currentProject.MigrationResult.NewVersion;
        
        if (!string.IsNullOrEmpty(oldVersion) && 
            !string.IsNullOrEmpty(newVersion) && 
            oldVersion != newVersion)
        {
            // Populate the migration banner strings
            MigrationBannerTitle = _stringLocalizer.GetString("ConsolePanel_MigrationBannerTitle");
            MigrationBannerMessage = _stringLocalizer.GetString("ConsolePanel_MigrationBannerMessage", oldVersion, newVersion);
            IsMigrationBannerVisible = true;
            ShowConsolePanel(); 
        }
    }

    public void OnMigrationBannerClosed()
    {
        IsMigrationBannerVisible = false;
    }

    public void Cleanup()
    {
        _messengerService.UnregisterAll(this);
    }
}
