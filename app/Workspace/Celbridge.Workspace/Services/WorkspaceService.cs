using Celbridge.Activities;
using Celbridge.Console;
using Celbridge.DataTransfer;
using Celbridge.Documents;
using Celbridge.Entities;
using Celbridge.Explorer;
using Celbridge.GenerativeAI;
using Celbridge.Inspector;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Python;
using Celbridge.Search;
using Celbridge.UserInterface;

namespace Celbridge.Workspace.Services;

public class WorkspaceService : IWorkspaceService, IDisposable
{
    private readonly ILogger<WorkspaceService> _logger;
    private readonly IMessengerService _messengerService;

    public IWorkspaceSettingsService WorkspaceSettingsService { get; }
    public IWorkspaceSettings WorkspaceSettings => WorkspaceSettingsService.WorkspaceSettings!;
    public IResourceService ResourceService { get; }
    public IPythonService PythonService { get; }
    public IConsoleService ConsoleService { get; }
    public IDocumentsService DocumentsService { get; }
    public IInspectorService InspectorService { get; }
    public IExplorerService ExplorerService { get; }
    public ISearchService SearchService { get; }
    public IDataTransferService DataTransferService { get; }
    public IEntityService EntityService { get; }
    public IGenerativeAIService GenerativeAIService { get; }
    public IActivityService ActivityService { get; }

    public WorkspacePanel ActivePanel { get; set; }

    public IActivityPanel ActivityPanel { get; private set; } = null!;
    public IDocumentsPanel DocumentsPanel { get; private set; } = null!;
    public IInspectorPanel InspectorPanel { get; private set; } = null!;
    public IConsolePanel ConsolePanel { get; private set; } = null!;

    private bool _workspaceStateIsDirty;

    public WorkspaceService(
        IServiceProvider serviceProvider,
        ILogger<WorkspaceService> logger,
        IMessengerService messengerService,
        IProjectService projectService)
    {
        _logger = logger;
        _messengerService = messengerService;

        // Create instances of the required sub-services

        WorkspaceSettingsService = serviceProvider.GetRequiredService<IWorkspaceSettingsService>();
        ResourceService = serviceProvider.GetRequiredService<IResourceService>();
        PythonService = serviceProvider.GetRequiredService<IPythonService>();
        ConsoleService = serviceProvider.GetRequiredService<IConsoleService>();
        DocumentsService = serviceProvider.GetRequiredService<IDocumentsService>();
        InspectorService = serviceProvider.GetRequiredService<IInspectorService>();
        ExplorerService = serviceProvider.GetRequiredService<IExplorerService>();
        SearchService = serviceProvider.GetRequiredService<ISearchService>();
        DataTransferService = serviceProvider.GetRequiredService<IDataTransferService>();
        EntityService = serviceProvider.GetRequiredService<IEntityService>();
        GenerativeAIService = serviceProvider.GetRequiredService<IGenerativeAIService>();
        ActivityService = serviceProvider.GetRequiredService<IActivityService>();

        //
        // Let the workspace settings service know where to find the workspace settings database
        //

        var project = projectService.CurrentProject;
        Guard.IsNotNull(project);
        var workspaceSettingsFolder = Path.Combine(project.ProjectFolderPath, ProjectConstants.MetaDataFolder, ProjectConstants.CacheFolder);
        Guard.IsNotNullOrEmpty(workspaceSettingsFolder);
        WorkspaceSettingsService.WorkspaceSettingsFolderPath = workspaceSettingsFolder;

        _messengerService.Register<WorkspaceStateDirtyMessage>(this, OnWorkspaceStateDirtyMessage);
    }

    public void SetPanels(
        IActivityPanel activityPanel,
        IDocumentsPanel documentsPanel,
        IInspectorPanel inspectorPanel,
        IConsolePanel consolePanel)
    {
        // Store panel references
        ActivityPanel = activityPanel;
        DocumentsPanel = documentsPanel;
        InspectorPanel = inspectorPanel;
        ConsolePanel = consolePanel;
    }

    private void OnWorkspaceStateDirtyMessage(object recipient, WorkspaceStateDirtyMessage message)
    {
        _workspaceStateIsDirty = true;
    }

    public async Task<Result> UpdateWorkspaceAsync(double deltaTime)
    {
        bool failed = false;

        if (_workspaceStateIsDirty)
        {
            _workspaceStateIsDirty = false;

            // Todo: Save the workspace state after a delay to avoid saving too frequently
            var saveWorkspaceResult = await SaveWorkspaceStateAsync();
            if (saveWorkspaceResult.IsFailure)
            {
                failed = true;
                _logger.LogError($"Failed to save workspace state. {saveWorkspaceResult.Error}");
            }
        }

        var saveEntitiesResult = await EntityService.SaveEntitiesAsync();
        if (saveEntitiesResult.IsFailure)
        {
            failed = true;
            _logger.LogError($"Failed to save modified entities. {saveEntitiesResult.Error}");
        }

        var saveDocumentsResult = await DocumentsService.SaveModifiedDocuments(deltaTime);
        if (saveDocumentsResult.IsFailure)
        {
            failed = true;
            _logger.LogError($"Failed to save modified documents. {saveDocumentsResult.Error}");
        }

        var activitiesResult = await ActivityService.UpdateAsync();
        if (activitiesResult.IsFailure)
        {
            failed = true;
            _logger.LogError($"Failed to update activity service. {activitiesResult.Error}");
        }

        var inspectorResult = await InspectorService.UpdateAsync();
        if (inspectorResult.IsFailure)
        {
            failed = true;
            _logger.LogError($"Failed to update inspector service. {inspectorResult.Error}");
        }

        // Todo: Clear save icon on the status bar if there are no pending saves

        if (failed)
        {
            return Result.Fail("Failed to update workspace");
        }

        return Result.Ok();
    }

    private async Task<Result> SaveWorkspaceStateAsync()
    {
        var folderStateService = ExplorerService.FolderStateService;
        await folderStateService.SaveAsync();

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

                // Unregister message handlers
                _messengerService.UnregisterAll(this);

                // Dispose resource service first to stop file system monitoring
                (ResourceService as IDisposable)?.Dispose();
                (WorkspaceSettingsService as IDisposable)!.Dispose();
                (PythonService as IDisposable)!.Dispose();
                (ConsoleService as IDisposable)!.Dispose();
                (DocumentsService as IDisposable)!.Dispose();
                (InspectorService as IDisposable)!.Dispose();
                (ExplorerService as IDisposable)!.Dispose();
                (SearchService as IDisposable)!.Dispose();
                (DataTransferService as IDisposable)!.Dispose();
                (EntityService as IDisposable)!.Dispose();
                (GenerativeAIService as IDisposable)!.Dispose();
                (ActivityService as IDisposable)!.Dispose();
            }

            _disposed = true;
        }
    }

    ~WorkspaceService()
    {
        Dispose(false);
    }
}
