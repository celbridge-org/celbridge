using Celbridge.BaseLibrary.Workspace;

namespace Celbridge.BaseLibrary.UserInterface;

public interface IUserInterfaceService
{
    /// <summary>
    /// Returns the main window of the application.
    /// </summary>
    object MainWindow { get; }

    /// <summary>
    /// The list of registered workspace panel configurations.
    /// </summary>
    IEnumerable<WorkspacePanelConfig> WorkspacePanelConfigs { get; }

    /// <summary>
    /// Registers a workspace panel configuration.
    /// These configurations are used to create the workspace panels when the workspace is loaded.
    /// </summary>
    Result RegisterWorkspacePanelConfig(WorkspacePanelConfig workspacePanelConfig);

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
