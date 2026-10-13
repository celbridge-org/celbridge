using Celbridge.Logging;

namespace Celbridge.Projects.Services;

/// <summary>
/// Factory for creating Project instances.
/// </summary>
internal class ProjectFactory
{
    private readonly ILogger<ProjectFactory> _logger;
    private readonly IDataFolderService _dataFolderService;
    private readonly ILocalFileSystem _fileSystem;

    public ProjectFactory(
        ILogger<ProjectFactory> logger,
        IDataFolderService dataFolderService,
        ILocalFileSystem fileSystem)
    {
        _logger = logger;
        _dataFolderService = dataFolderService;
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Loads a project from the specified file path: parses its config, creates its data folder if it has
    /// none, and returns a populated Project.
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

        var dataFolderResult = await _dataFolderService.EnsureDataFolderAsync(projectFilePath);
        if (dataFolderResult.IsFailure)
        {
            return Result<IProject>.Fail($"Failed to load the project: '{projectFilePath}'")
                .WithErrors(dataFolderResult);
        }
        var projectDataFolderPath = dataFolderResult.Value;

        try
        {
            var projectName = Path.GetFileNameWithoutExtension(projectFilePath);
            var projectFolderPath = Path.GetDirectoryName(projectFilePath)!;

            bool migrationSucceeded = migrationResult.OperationResult.IsSuccess;

            if (!migrationSucceeded)
            {
                _logger.LogError(migrationResult.OperationResult, "Failed to migrate project to latest version of Celbridge.");
            }

            ProjectConfig config;
            bool configIsHealthy = false;
            Result? configLoadFailure = null;
            if (migrationSucceeded)
            {
                var parseResult = ProjectConfigParser.ParseFromFile(projectFilePath, _fileSystem);
                if (parseResult.IsFailure)
                {
                    _logger.LogError(parseResult, "Failed to parse project configuration");
                    config = new ProjectConfig();
                    configLoadFailure = parseResult;
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
                configLoadFailure = migrationResult.OperationResult;
            }

            var project = new Project(
                projectFilePath,
                projectName,
                projectFolderPath,
                projectDataFolderPath,
                config,
                migrationResult,
                configIsHealthy,
                configLoadFailure);

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
