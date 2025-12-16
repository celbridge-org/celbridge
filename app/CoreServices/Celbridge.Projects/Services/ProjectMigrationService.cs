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

    public Result<bool> CheckNeedsMigration(string projectFilePath)
    {
        try
        {
            if (!File.Exists(projectFilePath))
            {
                return Result<bool>.Fail($"Project file does not exist: '{projectFilePath}'");
            }

            var text = File.ReadAllText(projectFilePath);
            var parse = Toml.Parse(text);
            
            if (parse.HasErrors)
            {
                return Result<bool>.Fail($"Failed to parse project TOML file: '{projectFilePath}'");
            }

            var root = parse.ToModel();
            
            // Get project version from celbridge.version
            if (!JsonPointerToml.TryResolve(root, "/celbridge/version", out var versionNode, out _) ||
                versionNode is not string projectVersionStr)
            {
                // No version = needs migration
                _logger.LogInformation("Project has no [celbridge].version - migration required");
                return Result<bool>.Ok(true);
            }

            // Get current application version
            var envInfo = _utilityService.GetEnvironmentInfo();
            
            // Simple version comparison - if versions don't match, migration may be needed
            var needsMigration = projectVersionStr != envInfo.AppVersion;

            if (needsMigration)
            {
                _logger.LogInformation(
                    "Project migration needed: project version {ProjectVersion}, current version {CurrentVersion}",
                    projectVersionStr,
                    envInfo.AppVersion);
            }

            return Result<bool>.Ok(needsMigration);
        }
        catch (Exception ex)
        {
            return Result<bool>.Fail("Failed to check if project needs migration")
                .WithException(ex);
        }
    }

    public async Task<Result> MigrateProjectAsync(string projectFilePath)
    {
        try
        {
            _logger.LogInformation($"Starting project migration for: {projectFilePath}");

            var text = File.ReadAllText(projectFilePath);
            var parse = Toml.Parse(text);
            
            if (parse.HasErrors)
            {
                return Result.Fail($"Failed to parse project file: {string.Join("; ", parse.Diagnostics)}");
            }

            var root = parse.ToModel();
            
            // Get project version
            var projectVersion = string.Empty;
            if (JsonPointerToml.TryResolve(root, "/celbridge/version", out var versionNode, out _) &&
                versionNode is string existingVersion)
            {
                projectVersion = existingVersion;
            }

            // Get current application version
            var envInfo = _utilityService.GetEnvironmentInfo();
            var currentVersionStr = envInfo.AppVersion;

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
