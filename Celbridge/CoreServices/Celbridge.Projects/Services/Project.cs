using Celbridge.Logging;

using Path = System.IO.Path;

namespace Celbridge.Projects.Services;

public class Project : IDisposable, IProject
{
    private readonly ILogger<Project> _logger;

    private ProjectConfig? _projectConfig;
    public IProjectConfig ProjectConfig => _projectConfig!;

    private string? _projectFilePath;
    public string ProjectFilePath => _projectFilePath!;

    private string? _projectName;
    public string ProjectName => _projectName!;

    private string? _projectFolderPath;
    public string ProjectFolderPath => _projectFolderPath!;

    private string? _projectDataFolderPath;
    public string ProjectDataFolderPath => _projectDataFolderPath!;

    public Project(ILogger<Project> logger)
    {
        _logger = logger;
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
            var project = ServiceLocator.AcquireService<IProject>() as Project;
            Guard.IsNotNull(project);

            project.PopulatePaths(projectFilePath);

            //
            // Load project properties from the project file
            //

            var configJson = File.ReadAllText(projectFilePath);

            var projectConfig = ServiceLocator.AcquireService<IProjectConfig>() as ProjectConfig;
            Guard.IsNotNull(projectConfig);

            var initResult = projectConfig.Initialize(configJson);
            if (initResult.IsFailure)
            {
                return Result<IProject>.Fail($"Failed to initialize project configuration")
                    .WithErrors(initResult);
            }

            project._projectConfig = projectConfig;

            //
            // Load project database
            //

            if (!Directory.Exists(project.ProjectDataFolderPath))
            {
                project._logger.LogWarning($"Project data folder does not exist: '{project.ProjectDataFolderPath}'. Creating an empty folder.");
                Directory.CreateDirectory(project.ProjectDataFolderPath);
            }

            return Result<IProject>.Ok(project);
        }
        catch (Exception ex)
        {
            return Result<IProject>.Fail($"An exception occured when loading the project: {projectFilePath}")
                .WithException(ex);
        }
    }

    public static async Task<Result> CreateProjectAsync(string projectFilePath)
    {
        Guard.IsNotNullOrWhiteSpace(projectFilePath);

        try
        {
            if (string.IsNullOrEmpty(projectFilePath))
            {
                return Result.Fail("Project file path is empty");
            }

            if (File.Exists(projectFilePath))
            {
                return Result.Fail($"Project file already exists exist: {projectFilePath}");
            }

            var projectPath = Path.GetDirectoryName(projectFilePath);
            Guard.IsNotNull(projectPath);

            var projectDataFolderPath = Path.Combine(projectPath, ProjectConstants.ProjectDataFolder);

            if (!Directory.Exists(projectDataFolderPath))
            {
                Directory.CreateDirectory(projectDataFolderPath);
            }

            // Todo: Populate this with project configuration options
            await File.WriteAllTextAsync(projectFilePath, "{}");
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when creating the project: {projectFilePath}")
                .WithException(ex);
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

        _projectDataFolderPath = Path.Combine(ProjectFolderPath, ProjectConstants.ProjectDataFolder);
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
                // Dispose managed objects here
            }

            _disposed = true;
        }
    }

    ~Project()
    {
        Dispose(false);
    }
}
