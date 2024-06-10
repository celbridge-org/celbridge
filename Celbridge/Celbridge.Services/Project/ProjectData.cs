using SQLite;
using Celbridge.BaseLibrary.Project;

namespace Celbridge.Services.Project;

public class ProjectData : IDisposable, IProjectData
{

    private SQLiteAsyncConnection _connection;
    private bool _disposed = false;

    private ProjectData(string projectName, string databasePath)
    {
        Guard.IsNotNullOrWhiteSpace(projectName);
        Guard.IsNotNullOrWhiteSpace(databasePath);

        ProjectName = projectName;

        _connection = new SQLiteAsyncConnection(databasePath);
    }

    public string ProjectName { get; init; }

    public async Task<IProjectConfig> GetConfigAsync()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(_connection));
        }

        var config = await _connection.Table<ProjectConfig>().FirstOrDefaultAsync();
        if (config == null)
        {
            throw new InvalidOperationException("ProjectConfig table not found in database.");
        }

        return config;
    }

    public async Task SetConfigAsync(IProjectConfig config)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(_connection));
        }

        if (config == null)
        {
            throw new ArgumentNullException();
        }

        await _connection.InsertAsync(config);
    }

    public static Result<IProjectData> LoadProjectData(string projectName, string databasePath)
    {
        Guard.IsNotNullOrWhiteSpace(projectName);
        Guard.IsNotNullOrWhiteSpace(databasePath);

        var project = new ProjectData(projectName, databasePath);
        Guard.IsNotNull(project);

        return Result<IProjectData>.Ok(project);
    }

    public static async Task<Result> CreateProjectDataAsync(string projectName, string databasePath, int version)
    {
        Guard.IsNotNullOrWhiteSpace(projectName);
        Guard.IsNotNullOrWhiteSpace(databasePath);

        var project = new ProjectData(projectName, databasePath);
        Guard.IsNotNull(project);

        var config = new ProjectConfig 
        { 
            Version = version 
        };

        await project._connection.CreateTableAsync<ProjectConfig>();
        await project._connection.InsertAsync(config);

        // Close the database after creating it
        project.Dispose();

        return Result.Ok();
    }

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

    ~ProjectData()
    {
        Dispose(false);
    }
}
