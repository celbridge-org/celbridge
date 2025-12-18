using Celbridge.Logging;
using Celbridge.Utilities;
using System.Text.RegularExpressions;
using Tomlyn;
using Tomlyn.Model;
using Path = System.IO.Path;

namespace Celbridge.Projects.Services;

public class ProjectMigrationService : IProjectMigrationService
{
    private const string ApplicationVersionSentinel = "<application-version>";

    private readonly ILogger<ProjectMigrationService> _logger;
    private readonly IUtilityService _utilityService;
    private readonly MigrationStepRegistry _migrationRegistry;

    public ProjectMigrationService(
        ILogger<ProjectMigrationService> logger,
        IUtilityService utilityService,
        MigrationStepRegistry migrationRegistry)
    {
        _logger = logger;
        _utilityService = utilityService;
        _migrationRegistry = migrationRegistry;
        _migrationRegistry.Initialize();
    }

    public async Task<MigrationResult> PerformMigrationAsync(string projectFilePath)
    {
        try
        {
            if (!File.Exists(projectFilePath))
            {
                var errorResult = Result.Fail($"Project file does not exist: '{projectFilePath}'");
                return MigrationResult.FromStatus(MigrationStatus.Failed, errorResult);
            }

            var text = File.ReadAllText(projectFilePath);
            var parse = Toml.Parse(text);

            if (parse.HasErrors)
            {
                var errorResult = Result.Fail($"Failed to parse project TOML file: {string.Join("; ", parse.Diagnostics)}");
                return MigrationResult.FromStatus(MigrationStatus.InvalidConfig, errorResult);
            }

            var root = parse.ToModel();

            // Get project version from [celbridge].celbridge-version property
            var projectVersion = string.Empty;
            if (JsonPointerToml.TryResolve(root, "/celbridge/celbridge-version", out var versionNode, out _) &&
                versionNode is string existingVersion)
            {
                projectVersion = existingVersion;
            }
            // Fall back to pre-v0.1.5 format for backwards compatibility during migration
            else if (JsonPointerToml.TryResolve(root, "/celbridge/version", out var legacyVersionNode, out _) &&
                legacyVersionNode is string legacyVersion)
            {
                projectVersion = legacyVersion;
            }

            // Get current application version
            var envInfo = _utilityService.GetEnvironmentInfo();
            var applicationVersion = envInfo.AppVersion;

            // Attempt to resolve the migration by comparing the project and application versions.
            var resolved = TryResolveMigration(projectVersion, applicationVersion, out var resolveResult);
            if (resolved)
            {
                // Migration is either not needed or cannot proceed, so we can return now.
                Guard.IsNotNull(resolveResult);
                return resolveResult;
            }

            // Proceed to migrate the project to the latest version.
            return await MigrateProjectAsync(projectFilePath, projectVersion, applicationVersion, root);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform project migration");
            var errorResult = Result.Fail($"Failed to execute migration")
                .WithException(ex);
            return MigrationResult.FromStatus(MigrationStatus.Failed, errorResult);
        }
    }

    private bool TryResolveMigration(string projectVersion, string applicationVersion, out MigrationResult? result)
    {
        result = null;

        // The sentinel value "<application-version>" means "use current version" without updating the file.
        // This is for dev team use with example projects during development.
        bool usingSentinelVersion = projectVersion == ApplicationVersionSentinel;

        // Compare versions to determine if migration is needed
        var versionState = CompareVersions(projectVersion, applicationVersion);

        switch (versionState)
        {
            case VersionComparisonState.SameVersion:
            {
                // If using the "<application-version>" sentinel value, treat as same version but DO NOT update the project file
                if (usingSentinelVersion)
                {
                    _logger.LogInformation(
                        "Project version is sentinel '<application-version>' - treating as current version without updating file: {CurrentVersion}",
                        applicationVersion);

                    // Return the same app version for both old and new to suppress the upgrade notification banner
                    result = MigrationResult.WithVersions(MigrationStatus.Complete, Result.Ok(), applicationVersion, applicationVersion);
                    return true;
                }

                _logger.LogDebug("Project version matches application version: {Version}", applicationVersion);

                result = MigrationResult.WithVersions(MigrationStatus.Complete, Result.Ok(), applicationVersion, applicationVersion);
                return true;
            }

            case VersionComparisonState.OlderVersion:
            {
                _logger.LogInformation(
                    "Project migration needed: project version {ProjectVersion}, current version {CurrentVersion}",
                    projectVersion,
                    applicationVersion);

                // Migration was not resolved so we need to proceed to migrate the project.
                return false;
            }

            case VersionComparisonState.NewerVersion:
            {
                var errorResult = Result.Fail(
                    $"This project was created with a newer version of Celbridge (v{projectVersion}). " +
                    $"Your current Celbridge version is v{applicationVersion}. " +
                    $"Please upgrade Celbridge or correct the version number in the .celbridge file.");

                result = MigrationResult.FromStatus(MigrationStatus.IncompatibleVersion, errorResult);
                return true;
            }

            case VersionComparisonState.InvalidVersion:
            {
                var errorResult = Result.Fail(
                    $"Project version '{projectVersion}' or application version '{applicationVersion}' is not in a recognized format. " +
                    $"Please correct the version number in the .celbridge file and reload the project.");
                result = MigrationResult.FromStatus(MigrationStatus.InvalidVersion, errorResult);
                return true;
            }

            default:
            {
                var errorResult = Result.Fail($"Unknown version comparison state: {versionState}");
                result = MigrationResult.FromStatus(MigrationStatus.Failed, errorResult);
                return true;
            }
        }
    }

    private async Task<MigrationResult> MigrateProjectAsync(string projectFilePath, string projectVersion, string applicationVersion, TomlTable root)
    {
        // Perform migration using step-based approach
        _logger.LogInformation($"Starting project migration for: {projectFilePath}");

        var projectVer = new Version(NormalizeVersion(projectVersion));
        var applicationVer = new Version(applicationVersion);

        // Get the list of steps required to migrate from current version to application version
        var requiredSteps = _migrationRegistry.GetRequiredSteps(projectVer, applicationVer);
                
        if (requiredSteps.Count == 0)
        {
            _logger.LogInformation("No migration steps required");
                    
            // We still need to update the version number if it differs
            if (projectVersion != applicationVersion)
            {
                var writeResult = await WriteApplicationVersionAsync(projectFilePath, projectVersion, applicationVersion);
                if (writeResult.IsFailure)
                {
                    var errorResult = Result.Fail($"Failed to write application version to project file: '{projectFilePath}'");
                    return MigrationResult.FromStatus(MigrationStatus.Failed, errorResult);
                }
            }
                    
            return MigrationResult.WithVersions(MigrationStatus.Complete, Result.Ok(), projectVersion, applicationVersion);
        }
                
        _logger.LogInformation($"Executing {requiredSteps.Count} migration steps");
                
        // Create migration context
        var projectFolderPath = Path.GetDirectoryName(projectFilePath)!;
        var projectDataFolderPath = Path.Combine(projectFolderPath, ProjectConstants.MetaDataFolder);

        // Local function to write the project file.
        // Line endings are normalized for the current platform.
        Func<string, Task<Result>> writeProjectFileAsync = async (content) =>
        {
            try
            {
                // Normalize line endings to platform standard before writing
                var normalizedContent = content.Replace("\r\n", "\n").Replace("\r", "\n");
                normalizedContent = normalizedContent.Replace("\n", Environment.NewLine);

                await File.WriteAllTextAsync(projectFilePath, normalizedContent);
                return Result.Ok();
            }
            catch (Exception ex)
            {
                return Result.Fail("Failed to write project file")
                    .WithException(ex);
            }
        };

        var context = new MigrationContext
        {
            ProjectFilePath = projectFilePath,
            ProjectFolderPath = projectFolderPath,
            ProjectDataFolderPath = projectDataFolderPath,
            Configuration = root,
            Logger = _logger,
            OriginalVersion = projectVersion,
            WriteProjectFileAsync = writeProjectFileAsync
        };
                
        // Execute migration steps in order
        string currentVersion = projectVersion;
        foreach (var step in requiredSteps)
        {
            _logger.LogInformation($"Applying migration step: {step.GetType().Name} (Target: {step.TargetVersion})");
                    
            var stepResult = await step.ApplyAsync(context);
            if (stepResult.IsFailure)
            {
                var errorResult = Result.Fail($"Migration step {step.GetType().Name} failed")
                    .WithErrors(stepResult);
                return MigrationResult.FromStatus(MigrationStatus.Failed, errorResult);
            }

            // Update the celbridge-version in the config file to reflect the new version after each step
            var stepVersionString = step.TargetVersion.ToString();
            var versionUpdateResult = await WriteApplicationVersionAsync(projectFilePath, currentVersion, stepVersionString);
            if (versionUpdateResult.IsFailure)
            {
                var errorResult = Result.Fail($"Failed to update version after migration step {step.GetType().Name}")
                    .WithErrors(versionUpdateResult);
                return MigrationResult.FromStatus(MigrationStatus.Failed, errorResult);
            }

            currentVersion = stepVersionString;
            _logger.LogInformation($"Successfully applied migration step to version {currentVersion}");
                                        
            // Refresh the configuration after each step so subsequent steps see the updated state
            var readResult = await ReadProjectConfigAsync(projectFilePath);
            if (readResult.IsFailure)
            {
                var errorResult = Result.Fail($"Failed to read project configuration after migration step {step.GetType().Name}")
                    .WithErrors(readResult);
                return MigrationResult.FromStatus(MigrationStatus.Failed, errorResult);
            }

            context.Configuration = readResult.Value;
        }

        // Update the celbridge-version in the config file to reflect the current application version
        // Only modify the file if it's not already at the required version
        var finalVersion = applicationVersion;
        if (currentVersion != finalVersion)
        {
            var writeResult = await WriteApplicationVersionAsync(projectFilePath, currentVersion, finalVersion);
            if (writeResult.IsFailure)
            {
                var errorResult = Result.Fail($"Failed to write final application version to project file: '{projectFilePath}'");
                return MigrationResult.FromStatus(MigrationStatus.Failed, errorResult);
            }
        }

        _logger.LogInformation($"Project migration completed successfully: {projectVersion} >> {finalVersion}");
                
        return MigrationResult.WithVersions(MigrationStatus.Complete, Result.Ok(), projectVersion, finalVersion);
    }

    /// <summary>
    /// Compare two version strings in the format "major.minor.patch".
    /// Returns a VersionComparisonState indicating the relationship between the versions.
    /// The sentinel value "<application-version>" for projectVersion is treated as "use current version".
    /// This is useful for the Celbridge dev team working with example projects.
    /// </summary>
    private VersionComparisonState CompareVersions(string projectVersion, string applicationVersion)
    {
        // Handle the sentinel value "<application-version>" meaning "use current version"
        // This allows the Celbridge dev team to work with example projects during development without modifying the version in the file.
        if (projectVersion == ApplicationVersionSentinel)
        {
            _logger.LogInformation("Project version '<application-version>' - using current application version");
            return VersionComparisonState.SameVersion;
        }
        
        // Handle null or whitespace-only project version - we can't safely upgrade in this case.
        if (string.IsNullOrWhiteSpace(projectVersion))
        {
            _logger.LogError("Project version is empty - cannot determine compatibility");
            return VersionComparisonState.InvalidVersion;
        }

        // Handle empty/null application version - this should never happen, but fail safely
        if (string.IsNullOrWhiteSpace(applicationVersion))
        {
            _logger.LogError("Application version is empty - cannot determine compatibility");
            return VersionComparisonState.InvalidVersion;
        }

        try
        {
            // Normalize versions to 3-part format (major.minor.patch)
            var normalizedProjectVersion = NormalizeVersion(projectVersion);
            var normalizedAppVersion = NormalizeVersion(applicationVersion);
            
            var projectVer = new Version(normalizedProjectVersion);
            var appVer = new Version(normalizedAppVersion);
            
            int comparison = projectVer.CompareTo(appVer);
            
            if (comparison < 0)
            {
                return VersionComparisonState.OlderVersion;
            }
            else if (comparison > 0)
            {
                return VersionComparisonState.NewerVersion;
            }
            else
            {
                return VersionComparisonState.SameVersion;
            }
        }
        catch (ArgumentException ex)
        {
            // Version string format is invalid
            _logger.LogWarning(
                ex,
                "Failed to parse version strings - ProjectVersion: '{ProjectVersion}', ApplicationVersion: '{ApplicationVersion}'",
                projectVersion,
                applicationVersion);
            return VersionComparisonState.InvalidVersion;
        }
        catch (Exception ex)
        {
            // Unexpected error during version comparison
            _logger.LogError(
                ex,
                "Unexpected error comparing versions - ProjectVersion: '{ProjectVersion}', ApplicationVersion: '{ApplicationVersion}'",
                projectVersion,
                applicationVersion);
            return VersionComparisonState.InvalidVersion;
        }
    }

    /// <summary>
    /// Normalize a version string to 3-part format (major.minor.patch).
    /// 4-part versions are truncated to 3-part format (for legacy compatibility).
    /// </summary>
    private string NormalizeVersion(string versionString)
    {
        var parts = versionString.Split('.');
        
        if (parts.Length == 3)
        {
            // Modern 3-part format - validate and return
            if (!int.TryParse(parts[0], out int major) || major < 0 ||
                !int.TryParse(parts[1], out int minor) || minor < 0 ||
                !int.TryParse(parts[2], out int patch) || patch < 0)
            {
                throw new ArgumentException(
                    $"Version string '{versionString}' contains invalid numeric parts. All parts must be non-negative integers.");
            }
            
            return $"{major}.{minor}.{patch}";
        }
        else if (parts.Length == 4)
        {
            // Legacy 4-part format - truncate to 3-part
            if (!int.TryParse(parts[0], out int major) || major < 0 ||
                !int.TryParse(parts[1], out int minor) || minor < 0 ||
                !int.TryParse(parts[2], out int patch) || patch < 0)
            {
                throw new ArgumentException(
                    $"Version string '{versionString}' contains invalid numeric parts. All parts must be non-negative integers.");
            }
            
            var normalized3Part = $"{major}.{minor}.{patch}";
            
            _logger.LogInformation(
                "Legacy 4-part version '{Version}' detected. Truncating to 3-part format: {NormalizedVersion}",
                versionString,
                normalized3Part);
            
            return normalized3Part;
        }
        else
        {
            throw new ArgumentException(
                $"Version string must have exactly 3 or 4 parts, but '{versionString}' has {parts.Length} parts.");
        }
    }

    private async Task<Result> WriteApplicationVersionAsync(string projectFilePath, string projectVersion, string applicationVersion)
    {
        try
        {
            var originalText = await File.ReadAllTextAsync(projectFilePath);
            
            // Normalize to \n for processing
            var normalizedText = originalText.Replace("\r\n", "\n").Replace("\r", "\n");
            
            var updatedText = normalizedText;
            
            // Update existing celbridge-version line in [celbridge] section
            // Pattern matches: optional whitespace, celbridge-version, =, quoted version
            var pattern = @"^(\s*)celbridge-version\s*=\s*""[^""]*""";
            var match = Regex.Match(updatedText, pattern, RegexOptions.Multiline);
            
            if (match.Success)
            {
                // Preserve the original indentation from capture group 1
                var leadingWhitespace = match.Groups[1].Value;
                updatedText = Regex.Replace(
                    updatedText, 
                    pattern, 
                    $"{leadingWhitespace}celbridge-version = \"{applicationVersion}\"",
                    RegexOptions.Multiline);
            }
            else
            {
                // No existing celbridge-version line found
                // This should only happen if the file is corrupted or in old format
                return Result.Fail("Cannot update version: no celbridge-version line found in project file");
            }
            
            // Only write if content actually changed
            if (updatedText != normalizedText)
            {
                // Normalize line endings to platform standard before writing
                updatedText = updatedText.Replace("\n", Environment.NewLine);
                
                await File.WriteAllTextAsync(projectFilePath, updatedText);
                _logger.LogInformation("Updated project file with application version {ApplicationVersion}", applicationVersion);
            }
            
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail("Failed to write application version to project file")
                .WithException(ex);
        }
    }

    private async Task<Result<TomlTable>> ReadProjectConfigAsync(string projectFilePath)
    {
        try
        {
            var text = await File.ReadAllTextAsync(projectFilePath);
            var parse = Toml.Parse(text);
            
            if (parse.HasErrors)
            {
                return Result<TomlTable>.Fail($"Failed to parse project TOML file: {string.Join("; ", parse.Diagnostics)}");
            }

            var root = parse.ToModel();
            return Result<TomlTable>.Ok(root);
        }
        catch (Exception ex)
        {
            return Result<TomlTable>.Fail("Failed to refresh configuration from project file")
                .WithException(ex);
        }
    }
}
