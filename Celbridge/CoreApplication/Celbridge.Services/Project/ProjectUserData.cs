using SQLite;
using Celbridge.BaseLibrary.Project;

namespace Celbridge.Services.Project;

public class ProjectUserData : IDisposable, IProjectUserData
{
    private SQLiteAsyncConnection _connection;

    public string DatabasePath { get; init; }

    private ProjectUserData(string databasePath)
    {
        Guard.IsNotNullOrWhiteSpace(databasePath);
        DatabasePath = databasePath;

        _connection = new SQLiteAsyncConnection(databasePath);
    }

    public async Task<Result<int>> GetDataVersionAsync()
    {
        var dataVersion = await _connection.Table<DataVersion>().FirstOrDefaultAsync();
        if (dataVersion == null)
        {
            return Result<int>.Fail($"Failed to get data version");
        }

        return Result<int>.Ok(dataVersion.Version);
    }

    public async Task<Result> SetDataVersionAsync(int version)
    {
        var dataVersion = await _connection.Table<DataVersion>().FirstOrDefaultAsync();
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

    public static Result<IProjectUserData> LoadProjectUserData(string databasePath)
    {
        Guard.IsNotNullOrWhiteSpace(databasePath);

        var projectUserData = new ProjectUserData(databasePath);
        Guard.IsNotNull(projectUserData);

        return Result<IProjectUserData>.Ok(projectUserData);
    }

    public static async Task<Result> CreateProjectUserDataAsync(string databasePath, int version)
    {
        Guard.IsNotNullOrWhiteSpace(databasePath);

        var projectUserData = new ProjectUserData(databasePath);
        Guard.IsNotNull(projectUserData);

        await projectUserData._connection.CreateTableAsync<DataVersion>();

        var dataVersion = new DataVersion 
        { 
            Version = version 
        };
        await projectUserData._connection.InsertAsync(dataVersion);

        await projectUserData._connection.CreateTableAsync<ExpandedFolder>();

        // Close the database
        projectUserData.Dispose();

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

    ~ProjectUserData()
    {
        Dispose(false);
    }
}
