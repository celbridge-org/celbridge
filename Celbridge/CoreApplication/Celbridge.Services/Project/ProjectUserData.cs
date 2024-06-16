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

    public async Task<Result<IDataVersion>> GetDataVersionAsync()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(_connection));
        }

        var config = await _connection.Table<DataVersion>().FirstOrDefaultAsync();
        if (config == null)
        {
            return Result<IDataVersion>.Fail($"Failed to load {nameof(DataVersion)} table");
        }

        return Result<IDataVersion>.Ok(config);
    }

    public async Task SetDataVersionAsync(IDataVersion dataVersion)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(_connection));
        }

        if (dataVersion == null)
        {
            throw new ArgumentNullException();
        }

        await _connection.InsertAsync(dataVersion);
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
