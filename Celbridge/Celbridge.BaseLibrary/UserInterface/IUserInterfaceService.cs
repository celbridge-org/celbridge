using Celbridge.BaseLibrary.UserInterface.Dialog;
using Celbridge.BaseLibrary.UserInterface.FilePicker;
using Celbridge.BaseLibrary.Workspace;

namespace Celbridge.BaseLibrary.UserInterface;

public interface IUserInterfaceService
{
    /// <summary>
    /// Returns the main window of the application.
    /// </summary>
    object MainWindow { get; }

    /// <summary>
    /// Returns the XamlRoot of the the application.
    /// This is initialized with the XamlRoot property of the application's RootFrame during startup.
    /// </summary>
    object XamlRoot { get; }

    /// <summary>
    /// Returns trues if a project workspace is currently loaded.
    /// </summary>
    bool IsWorkspaceLoaded { get; }

    /// <summary>
    /// The workspace that is currently loaded.
    /// Attempting to access this property when no workspace is loaded will throw an InvalidOperationException.
    /// </summary>
    IWorkspaceService WorkspaceService { get; }

    /// <summary>
    /// Service for displaying the system file and folder picker dialog.
    /// </summary>
    IFilePickerService FilePickerService { get; }

    /// <summary>
    /// Service for displaying various dialogs, e.g. alerts, confirmation, modal progress
    /// </summary>
    IDialogService DialogService { get; }
}
