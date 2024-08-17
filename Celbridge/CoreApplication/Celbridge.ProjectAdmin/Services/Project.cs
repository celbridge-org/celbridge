using Celbridge.ProjectAdmin.Models;
using Celbridge.Projects;
using SQLite;

namespace Celbridge.ProjectAdmin.Services;

public class Project : IDisposable, IProject
{
    private const int DataVersion = 1;

    private SQLiteAsyncConnection _connection;

    public string ProjectName { get; init; }
    public string ProjectFilePath { get; init; }
    public string ProjectFolderPath { get; init; }
    public string DatabasePath { get; init; }
    public string LogFolderPath { get; init; }

    private Project(string projectFilePath, string databasePath, string logFolderPath)
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

    public static Result<IProject> LoadProject(string projectPath, string databasePath, string logFolderPath)
    {
        Guard.IsNotNullOrWhiteSpace(projectPath);
        Guard.IsNotNullOrWhiteSpace(databasePath);
        Guard.IsNotNullOrWhiteSpace(logFolderPath);

        var project = new Project(projectPath, databasePath, logFolderPath);
        Guard.IsNotNull(project);

        return Result<IProject>.Ok(project);
    }

    public static async Task<Result> CreateProjectAsync(string projectFilePath, string databasePath, string logFolderPath)
    {
        Guard.IsNotNullOrWhiteSpace(databasePath);

        var project = new Project(projectFilePath, databasePath, logFolderPath);
        Guard.IsNotNull(project);

        var dataVersion = new ProjectDataVersion 
        { 
            Version = DataVersion 
        };

        await project._connection.CreateTableAsync<ProjectDataVersion>();
        await project._connection.InsertAsync(dataVersion);

        // Close the database
        project.Dispose();

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

    ~Project()
    {
        Dispose(false);
    }
}
