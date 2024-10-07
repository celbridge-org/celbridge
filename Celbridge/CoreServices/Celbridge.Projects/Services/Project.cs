using Celbridge.Projects.Models;
using SQLite;

using Path = System.IO.Path;

namespace Celbridge.Projects.Services;

public class Project : IDisposable, IProject
{
    private const int DataVersion = 1;

    private ProjectConfig? _projectConfig;
    public IProjectConfig ProjectConfig => _projectConfig!;

    private SQLiteAsyncConnection? _connection;
    private SQLiteAsyncConnection Connection => _connection!;

    private string? _projectFilePath;
    public string ProjectFilePath => _projectFilePath!;

    private string? _projectName;
    public string ProjectName => _projectName!;

    private string? _projectFolderPath;
    public string ProjectFolderPath => _projectFolderPath!;

    private string? _projectDataFolderPath;
    public string ProjectDataFolderPath => _projectDataFolderPath!;

    public async Task<Result<int>> GetDataVersionAsync()
    {
        var dataVersion = await Connection.Table<ProjectDataVersion>().FirstOrDefaultAsync();
        if (dataVersion == null)
        {
            return Result<int>.Fail($"Failed to get data version for Project Data");
        }

        return Result<int>.Ok(dataVersion.Version);
    }

    public async Task<Result> SetDataVersionAsync(int version)
    {
        var dataVersion = await Connection.Table<ProjectDataVersion>().FirstOrDefaultAsync();
        if (dataVersion == null)
        {
            return Result.Fail($"Failed to set data version for Project Data");
        }

        dataVersion.Version = version;

        await Connection.UpdateAsync(dataVersion);
        
        return Result.Ok();
    }

    public static Result<IProject> LoadProject(string projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
        {
            return Result<IProject>.Fail("Project file path is empty");
        }

        if (!File.Exists(projectFilePath))
        {
            return Result<IProject>.Fail($"Project file does not exist: '{projectFilePath}'");
        }

        try
        {
            var serviceProvider = ServiceLocator.ServiceProvider;
            var project = serviceProvider.GetRequiredService<IProject>() as Project;
            Guard.IsNotNull(project);

            project.PopulatePaths(projectFilePath);

            //
            // Load project properties from the project file
            //

            var configJson = File.ReadAllText(projectFilePath);

            var projectConfig = serviceProvider.GetRequiredService<IProjectConfig>() as ProjectConfig;
            Guard.IsNotNull(projectConfig);

            var initResult = projectConfig.Initialize(configJson);
            if (initResult.IsFailure)
            {
                var failure = Result<IProject>.Fail($"Failed to initialize project configuration");
                failure.MergeErrors(initResult);
                return failure;
            }

            project._projectConfig = projectConfig;

            //
            // Load project database
            //

            if (!Directory.Exists(project.ProjectDataFolderPath))
            {
                return Result<IProject>.Fail($"Project data folder does not exist: '{project.ProjectDataFolderPath}'");
            }

            var projectDataFilePath = Path.Combine(project.ProjectDataFolderPath, FileNameConstants.ProjectDataFile);
            project._connection = new SQLiteAsyncConnection(projectDataFilePath);

            return Result<IProject>.Ok(project);
        }
        catch (Exception ex)
        {
            return Result<IProject>.Fail(ex, $"An exception occured when loading the project: {projectFilePath}");
        }
    }

    public static async Task<Result> CreateProjectAsync(string projectFilePath)
    {
        Guard.IsNotNullOrWhiteSpace(projectFilePath);

        // Todo: Create the data files in a temp directory first and move them into place when all operations succeed

        try
        {
            var serviceProvider = ServiceLocator.ServiceProvider;
            using (var project = serviceProvider.GetRequiredService<IProject>() as Project)
            {
                Guard.IsNotNull(project);

                if (string.IsNullOrEmpty(projectFilePath))
                {
                    return Result.Fail("Project file path is empty");
                }

                if (File.Exists(projectFilePath))
                {
                    return Result.Fail($"Project file already exists exist: {projectFilePath}");
                }

                project.PopulatePaths(projectFilePath);

                if (!Directory.Exists(project.ProjectDataFolderPath))
                {
                    // Create the project folder if it doesn't already exist
                    Directory.CreateDirectory(project.ProjectDataFolderPath);
                }

                // Todo: Populate this with project configuration options
                File.WriteAllText(projectFilePath, "{}");

                var projectDataFilePath = Path.Combine(project.ProjectDataFolderPath, FileNameConstants.ProjectDataFile);
                project._connection = new SQLiteAsyncConnection(projectDataFilePath);

                var dataVersion = new ProjectDataVersion
                {
                    Version = DataVersion
                };

                // Initialize the project data
                await project._connection.CreateTableAsync<ProjectDataVersion>();
                await project._connection.InsertAsync(dataVersion);
            }
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, $"An exception occurred when creating the project: {projectFilePath}");
        }

        return Result.Ok();
    }

    private void PopulatePaths(string projectFilePath)
    {
        _projectFilePath = projectFilePath;

        _projectName = Path.GetFileNameWithoutExtension(projectFilePath);
        Guard.IsNotNullOrWhiteSpace(ProjectName);

        _projectFolderPath = Path.GetDirectoryName(projectFilePath)!;
        Guard.IsNotNullOrWhiteSpace(ProjectFolderPath);

        _projectDataFolderPath = Path.Combine(ProjectFolderPath, FileNameConstants.ProjectDataFolder);
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
