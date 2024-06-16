using SQLite;
using Celbridge.BaseLibrary.Project;

namespace Celbridge.Services.Project;

public class ProjectData : IDisposable, IProjectData
{
    private SQLiteAsyncConnection _connection;

    public string ProjectName { get; init; }
    public string ProjectFilePath { get; init; }
    public string ProjectFolder { get; init; }
    public string DatabasePath { get; init; }

    private ProjectData(string projectFilePath, string databasePath)
    {
        Guard.IsNotNullOrWhiteSpace(projectFilePath);
        Guard.IsNotNullOrWhiteSpace(databasePath);

        ProjectName = Path.GetFileNameWithoutExtension(projectFilePath);
        Guard.IsNotNullOrWhiteSpace(ProjectName);

        ProjectFolder = Path.GetDirectoryName(projectFilePath)!;
        Guard.IsNotNullOrWhiteSpace(ProjectFolder);

        ProjectFilePath = projectFilePath;
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

        await _connection.InsertAsync(dataVersion);
    }

    public static Result<IProjectData> LoadProjectData(string projectPath, string databasePath)
    {
        Guard.IsNotNullOrWhiteSpace(projectPath);
        Guard.IsNotNullOrWhiteSpace(databasePath);

        var project = new ProjectData(projectPath, databasePath);
        Guard.IsNotNull(project);

        return Result<IProjectData>.Ok(project);
    }

    public static async Task<Result> CreateProjectDataAsync(string projectFilePath, string databasePath, int version)
    {
        Guard.IsNotNullOrWhiteSpace(databasePath);

        var projectData = new ProjectData(projectFilePath, databasePath);
        Guard.IsNotNull(projectData);

        var dataVersion = new DataVersion 
        { 
            Version = version 
        };

        await projectData._connection.CreateTableAsync<DataVersion>();
        await projectData._connection.InsertAsync(dataVersion);

        // Close the database after creating it
        projectData.Dispose();

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

    ~ProjectData()
    {
        Dispose(false);
    }
}
