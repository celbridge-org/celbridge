using Celbridge.Logging;

namespace Celbridge.Projects.Services;

/// <summary>
/// Factory for creating Project instances.
/// </summary>
public class ProjectFactory
{
    private readonly ILogger<ProjectFactory> _logger;
    private readonly ILocalFileSystem _fileSystem;

    public ProjectFactory(
        ILogger<ProjectFactory> logger,
        ILocalFileSystem fileSystem)
    {
        _logger = logger;
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Loads a project from the specified file path: parses its config and
    /// returns a populated Project. The legacy data folder is not created
    /// here; the entity service creates it on demand when an entity file is
    /// first written.
    /// </summary>
    public async Task<Result<IProject>> LoadAsync(string projectFilePath, MigrationResult migrationResult)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
        {
            return Result<IProject>.Fail("Project file path is empty");
        }

        var infoResult = await _fileSystem.GetInfoAsync(projectFilePath);
        if (infoResult.IsFailure || infoResult.Value.Kind != StorageItemKind.File)
        {
            return Result<IProject>.Fail($"Project file does not exist: '{projectFilePath}'");
        }

        try
        {
            var projectName = Path.GetFileNameWithoutExtension(projectFilePath);
            var projectFolderPath = Path.GetDirectoryName(projectFilePath)!;
            var projectDataFolderPath = Path.Combine(projectFolderPath, LegacyConstants.MetaDataFolder);

            bool migrationSucceeded = migrationResult.OperationResult.IsSuccess;

            if (!migrationSucceeded)
            {
                _logger.LogError(migrationResult.OperationResult, "Failed to migrate project to latest version of Celbridge.");
            }

            ProjectConfig config;
            bool configIsHealthy = false;
            if (migrationSucceeded)
            {
                var parseResult = ProjectConfigParser.ParseFromFile(projectFilePath, _fileSystem);
                if (parseResult.IsFailure)
                {
                    _logger.LogError(parseResult, "Failed to parse project configuration");
                    config = new ProjectConfig();
                }
                else
                {
                    config = parseResult.Value;
                    configIsHealthy = true;
                }
            }
            else
            {
                config = new ProjectConfig();
            }

            var project = new Project(
                projectFilePath,
                projectName,
                projectFolderPath,
                projectDataFolderPath,
                config,
                migrationResult,
                configIsHealthy);

            return Result<IProject>.Ok(project);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"An exception occurred when loading the project: {projectFilePath}");
            return Result<IProject>.Fail($"An exception occurred when loading the project: {projectFilePath}")
                .WithException(ex);
        }
    }
}
