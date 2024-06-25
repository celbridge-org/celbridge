using SQLite;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Diagnostics;
using Celbridge.Workspace.Models;

namespace Celbridge.Workspace.Services;

public class WorkspaceData : IDisposable, IWorkspaceData
{
    private const int DataVersion = 1;

    private SQLiteAsyncConnection _connection;

    public string DatabasePath { get; init; }

    private WorkspaceData(string databasePath)
    {
        Guard.IsNotNullOrWhiteSpace(databasePath);
        DatabasePath = databasePath;

        _connection = new SQLiteAsyncConnection(databasePath);
    }

    public async Task<Result<int>> GetDataVersionAsync()
    {
        var dataVersion = await _connection.Table<WorkspaceDataVersion>().FirstOrDefaultAsync();
        if (dataVersion == null)
        {
            return Result<int>.Fail($"Failed to get data version");
        }

        return Result<int>.Ok(dataVersion.Version);
    }

    public async Task<Result> SetDataVersionAsync(int version)
    {
        var dataVersion = await _connection.Table<WorkspaceDataVersion>().FirstOrDefaultAsync();
        if (dataVersion == null)
        {
            return Result.Fail($"Failed to get data version");
        }

        dataVersion.Version = version;

        await _connection.UpdateAsync(dataVersion);

        return Result.Ok();
    }

    public async Task<Result<List<string>>> GetExpandedFoldersAsync()
    {
        try
        {
            var expandedFolders = await _connection.Table<ExpandedFolder>().ToListAsync();
            var folderNames = expandedFolders.Select(ef => ef.Folder).ToList();

            return Result<List<string>>.Ok(folderNames);
        }
        catch (Exception ex)
        {
            return Result<List<string>>.Fail($"Failed to get expanded folders: {ex.Message}");
        }
    }

    public async Task<Result> SetExpandedFoldersAsync(List<string> folderNames)
    {
        try
        {
            await _connection.DeleteAllAsync<ExpandedFolder>();
            var expandedFolders = folderNames.Select(name => new ExpandedFolder { Folder = name }).ToList();
            await _connection.InsertAllAsync(expandedFolders);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to set expanded folders: {ex.Message}");
        }
    }

    public static Result<IWorkspaceData> LoadWorkspaceData(string databasePath)
    {
        Guard.IsNotNullOrWhiteSpace(databasePath);

        try
        {
            var workspaceData = new WorkspaceData(databasePath);
            Guard.IsNotNull(workspaceData);

            return Result<IWorkspaceData>.Ok(workspaceData);
        }
        catch (Exception ex)
        {
            return Result<IWorkspaceData>.Fail($"Failed to load workspace database. {ex.Message}");
        }
    }

    public static async Task<Result> CreateWorkspaceDataAsync(string databasePath)
    {
        Guard.IsNotNullOrWhiteSpace(databasePath);

        var workspaceData = new WorkspaceData(databasePath);
        Guard.IsNotNull(workspaceData);

        await workspaceData._connection.CreateTableAsync<WorkspaceDataVersion>();

        var dataVersion = new WorkspaceDataVersion 
        { 
            Version = DataVersion
        };
        await workspaceData._connection.InsertAsync(dataVersion);

        await workspaceData._connection.CreateTableAsync<ExpandedFolder>();

        // Close the database
        workspaceData.Dispose();

        return Result.Ok();
    }

    private bool _disposed = false;

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
                _connection?.CloseAsync().Wait();
            }

            _disposed = true;
        }
    }

    ~WorkspaceData()
    {
        Dispose(false);
    }
}
