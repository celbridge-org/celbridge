using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Workspace.Services;

public class WorkspaceDataService : IWorkspaceDataService, IDisposable
{
    private const string WorkspaceDatabaseName = "Workspace.db";

    public IWorkspaceData? LoadedWorkspaceData { get; private set; }

    public string? DatabaseFolder { get; set; }

    public async Task<Result> AcquireWorkspaceDataAsync()
    {
        //
        // Derive the workspace database path from the project database path
        //

        if (string.IsNullOrEmpty(DatabaseFolder))
        {
            return Result.Fail("Failed to acquire workspace data because database folder has not been set.");
        }
        var workspaceDatabasePath = Path.Combine(DatabaseFolder, WorkspaceDatabaseName);

        //
        // Create the workspace database if it doesn't exist yet
        //

        if (!File.Exists(workspaceDatabasePath))
        {
            var createResult = await CreateWorkspaceDataAsync(workspaceDatabasePath);
            if (createResult.IsFailure)
            {
                return createResult;
            }
        }

        //
        // Load the workspace database
        //

        var loadResult = LoadWorkspaceData(workspaceDatabasePath);
        if (loadResult.IsFailure)
        {
            return loadResult;
        }

        return Result.Ok();
    }

    public async Task<Result> CreateWorkspaceDataAsync(string databasePath)
    {
        try
        {
            var createResult = await WorkspaceData.CreateWorkspaceDataAsync(databasePath);
            return createResult;
        }
        catch (Exception ex) 
        {
            return Result.Fail($"Failed to create workspace database. {ex.Message}");
        }
    }

    public Result LoadWorkspaceData(string databasePath)
    {
        try
        {
            var loadResult = WorkspaceData.LoadWorkspaceData(databasePath);
            if (loadResult.IsFailure)
            {
                return Result.Fail($"Failed to load workspace database: {databasePath}");
            }

            LoadedWorkspaceData = loadResult.Value;

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to load workspace database. {ex.Message}");
        }
    }

    public Result UnloadWorkspaceData()
    {
        if (LoadedWorkspaceData is null)
        {
            Guard.IsNull(LoadedWorkspaceData);

            // Unloading a workspace that is not loaded is a no-op
            return Result.Ok();
        }

        var disposableWorkspaceData = LoadedWorkspaceData as IDisposable;
        Guard.IsNotNull(disposableWorkspaceData);
        disposableWorkspaceData.Dispose();
        LoadedWorkspaceData = null;

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
                if (LoadedWorkspaceData is not null)
                {
                    UnloadWorkspaceData();
                }
            }

            _disposed = true;
        }
    }

    ~WorkspaceDataService()
    {
        Dispose(false);
    }
}
