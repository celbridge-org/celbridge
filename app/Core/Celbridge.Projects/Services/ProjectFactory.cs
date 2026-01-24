using Celbridge.Logging;

namespace Celbridge.Projects.Services;

/// <summary>
/// Factory for creating Project instances using dependency injection.
/// </summary>
public class ProjectFactory
{
    private readonly ILogger<ProjectFactory> _logger;
    private readonly IServiceProvider _serviceProvider;

    public ProjectFactory(
        ILogger<ProjectFactory> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Loads a project from the specified file path.
    /// Creates data folder if missing, initializes config service, returns populated Project.
    /// </summary>
    public Task<Result<IProject>> LoadAsync(string projectFilePath, MigrationResult migrationResult)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
        {
            return Task.FromResult(Result<IProject>.Fail("Project file path is empty"));
        }

        if (!File.Exists(projectFilePath))
        {
            return Task.FromResult(Result<IProject>.Fail($"Project file does not exist: '{projectFilePath}'"));
        }

        try
        {
            // Compute paths
            var projectName = Path.GetFileNameWithoutExtension(projectFilePath);
            var projectFolderPath = Path.GetDirectoryName(projectFilePath)!;
            var projectDataFolderPath = Path.Combine(projectFolderPath, ProjectConstants.MetaDataFolder);

            bool migrationSucceeded = migrationResult.OperationResult.IsSuccess;

            if (!migrationSucceeded)
            {
                _logger.LogError(migrationResult.OperationResult, "Failed to migrate project to latest version of Celbridge.");
            }

            // Initialize project config service
            var projectConfig = _serviceProvider.AcquireService<IProjectConfigService>() as ProjectConfigService;
            Guard.IsNotNull(projectConfig);

            if (migrationSucceeded)
            {
                var initResult = projectConfig.InitializeFromFile(projectFilePath);
                if (initResult.IsFailure)
                {
                    _logger.LogError(initResult, "Failed to initialize project configuration");
                }
            }

            // Ensure project data folder exists
            if (!Directory.Exists(projectDataFolderPath))
            {
                Directory.CreateDirectory(projectDataFolderPath);
            }

            // Create the project instance
            var project = new Project(
                projectFilePath,
                projectName,
                projectFolderPath,
                projectDataFolderPath,
                projectConfig,
                migrationResult);

            return Task.FromResult(Result<IProject>.Ok(project));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"An exception occurred when loading the project: {projectFilePath}");
            return Task.FromResult(Result<IProject>.Fail($"An exception occurred when loading the project: {projectFilePath}")
                .WithException(ex));
        }
    }
}
