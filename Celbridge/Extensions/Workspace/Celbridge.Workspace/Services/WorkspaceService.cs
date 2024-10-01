using Celbridge.Console;
using Celbridge.DataTransfer;
using Celbridge.Documents;
using Celbridge.Inspector;
using Celbridge.Projects;
using Celbridge.Explorer;
using Celbridge.Scripting;
using Celbridge.Status;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Workspace.Services;

public class WorkspaceService : IWorkspaceService, IDisposable
{
    private const string ExpandedFoldersKey = "ExpandedFolders";

    public bool IsExplorerPanelVisible { get; }
    public bool IsInspectorPanelVisible { get; }
    public bool IsToolsPanelVisible { get; }

    public IWorkspaceDataService WorkspaceDataService { get; }
    public IScriptingService ScriptingService { get; }
    public IConsoleService ConsoleService { get; }
    public IDocumentsService DocumentsService { get; }
    public IInspectorService InspectorService { get; }
    public IExplorerService ExplorerService { get; }
    public IStatusService StatusService { get; }
    public IDataTransferService DataTransferService { get; }

    private IResourceRegistryDumper _resourceRegistryDumper;

    private bool _workspaceStateIsDirty;

    public WorkspaceService(
        IServiceProvider serviceProvider, 
        IProjectService projectService)
    {
        // Create instances of the required sub-services

        WorkspaceDataService = serviceProvider.GetRequiredService<IWorkspaceDataService>();
        ScriptingService = serviceProvider.GetRequiredService<IScriptingService>();
        ConsoleService = serviceProvider.GetRequiredService<IConsoleService>();
        DocumentsService = serviceProvider.GetRequiredService<IDocumentsService>();
        InspectorService = serviceProvider.GetRequiredService<IInspectorService>();
        ExplorerService = serviceProvider.GetRequiredService<IExplorerService>();
        StatusService = serviceProvider.GetRequiredService<IStatusService>();
        DataTransferService = serviceProvider.GetRequiredService<IDataTransferService>();

        //
        // Let the workspace data service know where to find the workspace database
        //

        var project = projectService.LoadedProject;
        Guard.IsNotNull(project);
        var databaseFolder = Path.GetDirectoryName(project.DatabasePath);
        Guard.IsNotNullOrEmpty(databaseFolder);
        WorkspaceDataService.DatabaseFolder = databaseFolder;

        // Dump the resource registry to a file in the logs folder
        string logFolderPath = project.LogFolderPath;
        _resourceRegistryDumper = serviceProvider.GetRequiredService<IResourceRegistryDumper>();
        _resourceRegistryDumper.Initialize(logFolderPath);
    }

    public void SetWorkspaceStateIsDirty()
    {
        _workspaceStateIsDirty = true;
    }

    public async Task<Result> FlushPendingSaves(double deltaTime)
    {
        // Todo: Save the workspace state after a delay to avoid saving too frequently

        if (_workspaceStateIsDirty)
        {
            _workspaceStateIsDirty = false;
            var saveWorkspaceResult = await SaveWorkspaceStateAsync();
            if (saveWorkspaceResult.IsFailure)
            {
                var failure = Result.Fail($"Failed to save workspace state");
                failure.MergeErrors(saveWorkspaceResult);
                return failure;
            }
        }

        var saveDocumentsResult = await DocumentsService.SaveModifiedDocuments(deltaTime);
        if (saveDocumentsResult.IsFailure)
        {
            var failure = Result.Fail($"Failed to save modified documents");
            failure.MergeErrors(saveDocumentsResult);
            return failure;
        }

        // Todo: Clear save icon on the status bar if there are no pending saves

        return Result.Ok();
    }

    private async Task<Result> SaveWorkspaceStateAsync()
    {
        var workspaceData = WorkspaceDataService.LoadedWorkspaceData;
        Guard.IsNotNull(workspaceData);

        // Save the expanded folders in the Resource Registry

        var resourceRegistry = ExplorerService.ResourceRegistry;
        var expandedFolders = resourceRegistry.ExpandedFolders;
        await workspaceData.SetPropertyAsync(ExpandedFoldersKey, expandedFolders);

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
                // We use the dispose pattern to ensure that the sub-services release all their resources when the project is closed.
                // This helps avoid memory leaks and orphaned objects/tasks when the user edits multiple projects during a session.

                (WorkspaceDataService as IDisposable)!.Dispose();
                (ScriptingService as IDisposable)!.Dispose();
                (ConsoleService as IDisposable)!.Dispose();
                (DocumentsService as IDisposable)!.Dispose();
                (InspectorService as IDisposable)!.Dispose();
                (ExplorerService as IDisposable)!.Dispose();
                (StatusService as IDisposable)!.Dispose();
                (DataTransferService as IDisposable)!.Dispose();
            }

            _disposed = true;
        }
    }

    ~WorkspaceService()
    {
        Dispose(false);
    }
}
