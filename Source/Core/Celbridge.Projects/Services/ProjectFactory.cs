using Celbridge.Logging;

namespace Celbridge.Projects.Services;

/// <summary>
/// Factory for creating Project instances.
/// </summary>
public class ProjectFactory
{
    private readonly ILogger<ProjectFactory> _logger;

    public ProjectFactory(ILogger<ProjectFactory> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Loads a project from the specified file path.
    /// Creates data folder if missing, parses config, returns populated Project.
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
            var projectName = Path.GetFileNameWithoutExtension(projectFilePath);
            var projectFolderPath = Path.GetDirectoryName(projectFilePath)!;
            var projectDataFolderPath = Path.Combine(projectFolderPath, ProjectConstants.MetaDataFolder);

            bool migrationSucceeded = migrationResult.OperationResult.IsSuccess;

            if (!migrationSucceeded)
            {
                _logger.LogError(migrationResult.OperationResult, "Failed to migrate project to latest version of Celbridge.");
            }

            ProjectConfig config;
            if (migrationSucceeded)
            {
                var parseResult = ProjectConfigParser.ParseFromFile(projectFilePath);
                if (parseResult.IsFailure)
                {
                    _logger.LogError(parseResult, "Failed to parse project configuration");
                    config = new ProjectConfig();
                }
                else
                {
                    config = parseResult.Value;
                }
            }
            else
            {
                config = new ProjectConfig();
            }

            if (!Directory.Exists(projectDataFolderPath))
            {
                Directory.CreateDirectory(projectDataFolderPath);
            }

            var project = new Project(
                projectFilePath,
                projectName,
                projectFolderPath,
                projectDataFolderPath,
                config,
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
