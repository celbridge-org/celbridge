using Celbridge.Clipboard;
using Celbridge.Commands;
using Celbridge.Console;
using Celbridge.Documents;
using Celbridge.Inspector;
using Celbridge.Projects;
using Celbridge.Status;
using Celbridge.Utilities;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Workspace.Services;

public class WorkspaceService : IWorkspaceService, IDisposable
{
    public bool IsLeftPanelVisible { get; }
    public bool IsRightPanelVisible { get; }
    public bool IsBottomPanelVisible { get; }

    public IWorkspaceDataService WorkspaceDataService { get; }
    public IConsoleService ConsoleService { get; }
    public IDocumentsService DocumentsService { get; }
    public IInspectorService InspectorService { get; }
    public IProjectService ProjectService { get; }
    public IStatusService StatusService { get; }
    public IClipboardService ClipboardService { get; }

    private ICommandLogger _commandLogger;

    public WorkspaceService(
        IServiceProvider serviceProvider, 
        IProjectDataService projectDataService,
        IUtilityService utilityService)
    {
        // Create instances of the required sub-services

        WorkspaceDataService = serviceProvider.GetRequiredService<IWorkspaceDataService>();
        ConsoleService = serviceProvider.GetRequiredService<IConsoleService>();
        DocumentsService = serviceProvider.GetRequiredService<IDocumentsService>();
        InspectorService = serviceProvider.GetRequiredService<IInspectorService>();
        ProjectService = serviceProvider.GetRequiredService<IProjectService>();
        StatusService = serviceProvider.GetRequiredService<IStatusService>();
        ClipboardService = serviceProvider.GetRequiredService<IClipboardService>();

        //
        // Let the workspace data service know where to find the workspace database
        //

        var projectData = projectDataService.LoadedProjectData;
        Guard.IsNotNull(projectData);
        var databaseFolder = Path.GetDirectoryName(projectData.DatabasePath);
        Guard.IsNotNullOrEmpty(databaseFolder);
        WorkspaceDataService.DatabaseFolder = databaseFolder;

        // Log executing commands
        _commandLogger = serviceProvider.GetRequiredService<ICommandLogger>();
        var timestamp = utilityService.GetTimestamp();
        string logFolderPath = projectData.LogFolderPath;
        string logFilePrefix = "CommandLog";
        string logFilename = $"{logFilePrefix}_{timestamp}.jsonl";
        string logFilePath = Path.Combine(logFolderPath, logFilename);

        _commandLogger.StartLogging(logFilePath, logFilePrefix, 0);
    }

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // We use the dispose pattern to ensure that the sub-services release all their resources when the project is closed.
                // This helps avoid memory leaks and orphaned objects/tasks when the user edits multiple projects during a session.

                (WorkspaceDataService as IDisposable)!.Dispose();
                (ConsoleService as IDisposable)!.Dispose();
                (DocumentsService as IDisposable)!.Dispose();
                (InspectorService as IDisposable)!.Dispose();
                (ProjectService as IDisposable)!.Dispose();
                (StatusService as IDisposable)!.Dispose();
                (ClipboardService as IDisposable)!.Dispose();

                // Stop logging commands
                (_commandLogger as IDisposable)!.Dispose();
            }

            _disposed = true;
        }
    }

    ~WorkspaceService()
    {
        Dispose(false);
    }
}
