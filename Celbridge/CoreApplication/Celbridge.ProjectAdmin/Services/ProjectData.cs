using SQLite;
using Celbridge.Projects;
using Celbridge.ProjectAdmin.Models;

namespace Celbridge.ProjectAdmin.Services;

public class ProjectData : IDisposable, IProjectData
{
    private const int DataVersion = 1;

    private SQLiteAsyncConnection _connection;

    public string ProjectName { get; init; }
    public string ProjectFilePath { get; init; }
    public string ProjectFolderPath { get; init; }
    public string DatabasePath { get; init; }
    public string LogFolderPath { get; init; }

    private ProjectData(string projectFilePath, string databasePath, string logFolderPath)
    {
        Guard.IsNotNullOrWhiteSpace(projectFilePath);
        Guard.IsNotNullOrWhiteSpace(databasePath);

        ProjectName = Path.GetFileNameWithoutExtension(projectFilePath);
        Guard.IsNotNullOrWhiteSpace(ProjectName);

        ProjectFolderPath = Path.GetDirectoryName(projectFilePath)!;
        Guard.IsNotNullOrWhiteSpace(ProjectFolderPath);

        ProjectFilePath = projectFilePath;
        DatabasePath = databasePath;
        LogFolderPath = logFolderPath;

        _connection = new SQLiteAsyncConnection(databasePath);
    }

    public async Task<Result<int>> GetDataVersionAsync()
    {
        var dataVersion = await _connection.Table<ProjectDataVersion>().FirstOrDefaultAsync();
        if (dataVersion == null)
        {
            return Result<int>.Fail($"Failed to get data version for Project Data");
        }

        return Result<int>.Ok(dataVersion.Version);
    }

    public async Task<Result> SetDataVersionAsync(int version)
    {
        var dataVersion = await _connection.Table<ProjectDataVersion>().FirstOrDefaultAsync();
        if (dataVersion == null)
        {
            return Result.Fail($"Failed to set data version for Project Data");
        }

        dataVersion.Version = version;

        await _connection.UpdateAsync(dataVersion);
        
        return Result.Ok();
    }

    public static Result<IProjectData> LoadProjectData(string projectPath, string databasePath, string logFolderPath)
    {
        Guard.IsNotNullOrWhiteSpace(projectPath);
        Guard.IsNotNullOrWhiteSpace(databasePath);
        Guard.IsNotNullOrWhiteSpace(logFolderPath);

        var project = new ProjectData(projectPath, databasePath, logFolderPath);
        Guard.IsNotNull(project);

        return Result<IProjectData>.Ok(project);
    }

    public static async Task<Result> CreateProjectDataAsync(string projectFilePath, string databasePath, string logFolderPath)
    {
        Guard.IsNotNullOrWhiteSpace(databasePath);

        var projectData = new ProjectData(projectFilePath, databasePath, logFolderPath);
        Guard.IsNotNull(projectData);

        var dataVersion = new ProjectDataVersion 
        { 
            Version = DataVersion 
        };

        await projectData._connection.CreateTableAsync<ProjectDataVersion>();
        await projectData._connection.InsertAsync(dataVersion);

        // Close the database
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
                _connection?.CloseAsync().Wait();
            }

            _disposed = true;
        }
    }

    ~ProjectData()
    {
        Dispose(false);
    }
}
