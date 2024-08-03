using Celbridge.Clipboard;
using Celbridge.Commands;
using Celbridge.Console;
using Celbridge.Documents;
using Celbridge.Inspector;
using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Status;
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

    private IMessengerService _messengerService;
    private IExecutedCommandLogger _commandLogger;
    private IResourceRegistryDumper _resourceRegistryDumper;

    public WorkspaceService(
        IServiceProvider serviceProvider, 
        IMessengerService messengerService,
        IProjectDataService projectDataService)
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

        // Log executed commands
        string logFolderPath = projectData.LogFolderPath;
        _commandLogger = serviceProvider.GetRequiredService<IExecutedCommandLogger>();
        _commandLogger.Initialize(logFolderPath, 0);

        // Dump the resource registry to a file
        _resourceRegistryDumper = serviceProvider.GetRequiredService<IResourceRegistryDumper>();
        _resourceRegistryDumper.Initialize(logFolderPath);

        _messengerService = messengerService;
        _messengerService.Register<RequestSaveWorkspaceStateMessage>(this, OnRequestSaveWorkspaceStateMessage);
    }

    private void OnRequestSaveWorkspaceStateMessage(object recipient, RequestSaveWorkspaceStateMessage message)
    {
        _ = SaveWorkspaceStateAsync();
    }

    public async Task<Result> SaveWorkspaceStateAsync()
    {
        // Save the expanded folders in the Resource Registry

        var resourceRegistry = ProjectService.ResourceRegistry;
        var expandedFolders = resourceRegistry.ExpandedFolders;

        var workspaceData = WorkspaceDataService.LoadedWorkspaceData;
        Guard.IsNotNull(workspaceData);

        var setFoldersResult = await workspaceData.SetExpandedFoldersAsync(expandedFolders);
        if (setFoldersResult.IsFailure)
        {
            return Result.Fail($"Failed to Save Workspace State. {setFoldersResult.Error}");
        }

        return Result.Ok();
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
                _messengerService.Unregister<RequestSaveWorkspaceStateMessage>(this);

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
