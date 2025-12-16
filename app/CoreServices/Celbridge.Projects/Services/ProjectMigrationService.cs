using Celbridge.Logging;
using Celbridge.Utilities;
using Tomlyn;

namespace Celbridge.Projects.Services;

public class ProjectMigrationService : IProjectMigrationService
{
    private readonly ILogger<ProjectMigrationService> _logger;
    private readonly IUtilityService _utilityService;

    public ProjectMigrationService(
        ILogger<ProjectMigrationService> logger,
        IUtilityService utilityService)
    {
        _logger = logger;
        _utilityService = utilityService;
    }

    public async Task<Result> PerformMigrationAsync(string projectFilePath)
    {
        try
        {
            if (!File.Exists(projectFilePath))
            {
                return Result.Fail($"Project file does not exist: '{projectFilePath}'");
            }

            var text = File.ReadAllText(projectFilePath);
            var parse = Toml.Parse(text);
            
            if (parse.HasErrors)
            {
                return Result.Fail($"Failed to parse project TOML file: {string.Join("; ", parse.Diagnostics)}");
            }

            var root = parse.ToModel();
            
            // Get project version from celbridge.version
            var projectVersion = string.Empty;
            if (JsonPointerToml.TryResolve(root, "/celbridge/version", out var versionNode, out _) &&
                versionNode is string existingVersion)
            {
                projectVersion = existingVersion;
            }

            // Get current application version
            var envInfo = _utilityService.GetEnvironmentInfo();
            var currentVersionStr = envInfo.AppVersion;
            
            // Check if migration is needed
            if (string.IsNullOrEmpty(projectVersion))
            {
                _logger.LogInformation("Project has no [celbridge].version - migration required");
            }
            else if (projectVersion == currentVersionStr)
            {
                // No migration needed - versions match
                return Result.Ok();
            }
            else
            {
                _logger.LogInformation(
                    "Project migration needed: project version {ProjectVersion}, current version {CurrentVersion}",
                    projectVersion,
                    currentVersionStr);
            }

            // Perform migration
            _logger.LogInformation($"Starting project migration for: {projectFilePath}");

            // Todo: Implement version migration logic

            await Task.CompletedTask;

            _logger.LogInformation("Project migration completed successfully");
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail("An exception occurred during project migration")
                .WithException(ex);
        }
    }
}
