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

        var dataVersion = new DataVersion 
        { 
            Version = version 
        };

        await projectUserData._connection.CreateTableAsync<DataVersion>();
        await projectUserData._connection.InsertAsync(dataVersion);

        // Close the database after creating it
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
                // Dispose managed resources
                _connection?.CloseAsync().Wait();
            }

            // Dispose unmanaged resources here if any

            _disposed = true;
        }
    }

    ~ProjectUserData()
    {
        Dispose(false);
    }
}
