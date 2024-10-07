using Celbridge.Console;
using Celbridge.DataTransfer;
using Celbridge.Documents;
using Celbridge.Inspector;
using Celbridge.Projects;
using Celbridge.Explorer;
using Celbridge.Scripting;
using Celbridge.Status;
using CommunityToolkit.Diagnostics;
using Celbridge.Settings;

namespace Celbridge.Workspace.Services;

public class WorkspaceService : IWorkspaceService, IDisposable
{
    private const string ExpandedFoldersKey = "ExpandedFolders";

    private readonly IEditorSettings _editorSettings;

    public IWorkspaceSettingsService WorkspaceSettingsService { get; }
    public IWorkspaceSettings WorkspaceSettings => WorkspaceSettingsService.WorkspaceSettings!;
    public IScriptingService ScriptingService { get; }
    public IConsoleService ConsoleService { get; }
    public IDocumentsService DocumentsService { get; }
    public IInspectorService InspectorService { get; }
    public IExplorerService ExplorerService { get; }
    public IStatusService StatusService { get; }
    public IDataTransferService DataTransferService { get; }

    private bool _workspaceStateIsDirty;

    private bool _showToolsPanelOnExitFocusMode;

    public WorkspaceService(
        IServiceProvider serviceProvider,
        IEditorSettings editorSettings,
        IProjectService projectService)
    {
        _editorSettings = editorSettings;

        // Create instances of the required sub-services

        WorkspaceSettingsService = serviceProvider.GetRequiredService<IWorkspaceSettingsService>();
        ScriptingService = serviceProvider.GetRequiredService<IScriptingService>();
        ConsoleService = serviceProvider.GetRequiredService<IConsoleService>();
        DocumentsService = serviceProvider.GetRequiredService<IDocumentsService>();
        InspectorService = serviceProvider.GetRequiredService<IInspectorService>();
        ExplorerService = serviceProvider.GetRequiredService<IExplorerService>();
        StatusService = serviceProvider.GetRequiredService<IStatusService>();
        DataTransferService = serviceProvider.GetRequiredService<IDataTransferService>();

        //
        // Let the workspace settings service know where to find the workspace settings database
        //

        var project = projectService.CurrentProject;
        Guard.IsNotNull(project);
        var workspaceSettingsFolder = Path.Combine(project.ProjectFolderPath, FileNameConstants.WorkspaceSettingsFolder);
        Guard.IsNotNullOrEmpty(workspaceSettingsFolder);
        WorkspaceSettingsService.WorkspaceSettingsFolderPath = workspaceSettingsFolder;
    }

    public void ToggleFocusMode()
    {
        // Are we in focus mode?
        bool isFocusModeActive = !_editorSettings.IsExplorerPanelVisible &&
            !_editorSettings.IsInspectorPanelVisible;

        if (isFocusModeActive)
        {
            // Exit focus mode
            _editorSettings.IsExplorerPanelVisible = true;
            _editorSettings.IsInspectorPanelVisible = true;

            if (_showToolsPanelOnExitFocusMode)
            {
                // Make the tools panel visible only if the flag was set when we entered focus mode
                _editorSettings.IsToolsPanelVisible = true;
                _showToolsPanelOnExitFocusMode = false;
            }
        }
        else
        {
            // Enter focus mode

            // Remember if we should make the tools panel visible when we exit focus mode
            _showToolsPanelOnExitFocusMode = _editorSettings.IsToolsPanelVisible;

            _editorSettings.IsExplorerPanelVisible = false;
            _editorSettings.IsInspectorPanelVisible = false;
            _editorSettings.IsToolsPanelVisible = false;
        }
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
        Guard.IsNotNull(WorkspaceSettings);

        // Save the expanded folders in the Resource Registry

        var resourceRegistry = ExplorerService.ResourceRegistry;
        var expandedFolders = resourceRegistry.ExpandedFolders;
        await WorkspaceSettings.SetPropertyAsync(ExpandedFoldersKey, expandedFolders);

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

                (WorkspaceSettingsService as IDisposable)!.Dispose();
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
