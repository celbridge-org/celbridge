using Celbridge.Logging;
using Celbridge.Utilities;
using System.Text.RegularExpressions;
using Tomlyn;

namespace Celbridge.Projects.Services;

/// <summary>
/// Represents the result of comparing a project version with the application version.
/// </summary>
public enum VersionComparisonState
{
    /// <summary>
    /// The project version matches the application version - no migration needed.
    /// </summary>
    SameVersion,
    
    /// <summary>
    /// The project version is older than the application version - migration needed.
    /// </summary>
    OlderVersion,
    
    /// <summary>
    /// The project version is newer than the application version - cannot open project.
    /// </summary>
    NewerVersion,
    
    /// <summary>
    /// Unable to determine version compatibility - cannot open project.
    /// </summary>
    UnresolvedVersion
}

public class ProjectMigrationService : IProjectMigrationService
{
    private const string ApplicationVersionSentinel = "<application-version>";

    private readonly ILogger<ProjectMigrationService> _logger;
    private readonly IUtilityService _utilityService;

    public ProjectMigrationService(
        ILogger<ProjectMigrationService> logger,
        IUtilityService utilityService)
    {
        _logger = logger;
        _utilityService = utilityService;
    }

    public async Task<MigrationResult> PerformMigrationAsync(string projectFilePath)
    {
        try
        {
            if (!File.Exists(projectFilePath))
            {
                var errorResult = Result.Fail($"Project file does not exist: '{projectFilePath}'");
                return MigrationResult.FromStatus(ProjectMigrationStatus.Failed, errorResult);
            }

            var text = File.ReadAllText(projectFilePath);
            var parse = Toml.Parse(text);
            
            if (parse.HasErrors)
            {
                var errorResult = Result.Fail($"Failed to parse project TOML file: {string.Join("; ", parse.Diagnostics)}");
                return MigrationResult.FromStatus(ProjectMigrationStatus.InvalidConfig, errorResult);
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
            var applicationVersion = envInfo.AppVersion;
            
            // Store the original project version for logging purposes
            var originalProjectVersion = projectVersion;
            
            // The sentinel value "<application-version>" means "use current version" without updating the file.
            // This is for dev team use with example projects during development.
            bool useSentinelVersion = projectVersion == ApplicationVersionSentinel;
            
            // Compare versions to determine if migration is needed
            var versionState = CompareVersions(projectVersion, applicationVersion);
            
            // Normalize application version to 3-part format 
            var normalizedAppVersion = NormalizeVersion(applicationVersion);
            
            switch (versionState)
            {
                case VersionComparisonState.SameVersion:
                {
                    // If using the "<application-version>", treat as same version but DO NOT update the project file
                    if (useSentinelVersion)
                    {
                        _logger.LogInformation(
                            "Project version is sentinel '<application-version>' - treating as current version without updating file: {CurrentVersion}",
                            normalizedAppVersion);
                        
                        // Return the normalized app version for both old and new to supress the upgrade notification banner
                        return MigrationResult.WithVersions(ProjectMigrationStatus.Success, Result.Ok(), normalizedAppVersion, normalizedAppVersion);
                    }
                    
                    // Even if versions are semantically the same, we should normalize the format
                    // in the file if it differs (e.g., "0.1.4.3" should become "0.1.4")
                    
                    // Check if the original project version differs from the normalized version
                    bool needsNormalization = !string.IsNullOrEmpty(originalProjectVersion) && 
                                              originalProjectVersion != normalizedAppVersion;
                    
                    if (needsNormalization)
                    {
                        _logger.LogInformation(
                            "Project version format needs normalization: {OriginalVersion} -> {NormalizedVersion}",
                            originalProjectVersion,
                            normalizedAppVersion);
                        
                        var normalizeResult = await WriteApplicationVersionAsync(projectFilePath, originalProjectVersion, normalizedAppVersion);
                        if (normalizeResult.IsFailure)
                        {
                            var errorResult = Result.Fail($"Failed to normalize version in project file: '{projectFilePath}'");
                            return MigrationResult.FromStatus(ProjectMigrationStatus.Failed, errorResult);
                        }
                        
                        // Return success with version information showing the normalization
                        return MigrationResult.WithVersions(ProjectMigrationStatus.Success, Result.Ok(), originalProjectVersion, normalizedAppVersion);
                    }
                    
                    _logger.LogDebug("Project version matches application version: {Version}", applicationVersion);
                    return MigrationResult.WithVersions(ProjectMigrationStatus.Success, Result.Ok(), projectVersion, applicationVersion);
                }
                
                case VersionComparisonState.OlderVersion:
                    _logger.LogInformation(
                        "Project migration needed: project version {ProjectVersion}, current version {CurrentVersion}",
                        originalProjectVersion,
                        applicationVersion);
                    break;
                
                case VersionComparisonState.NewerVersion:
                {
                    var errorResult = Result.Fail(
                        $"This project was created with a newer version of Celbridge (v{projectVersion}). " +
                        $"Your current version is v{applicationVersion}. " +
                        $"The project will load but Python initialization will be disabled. " +
                        $"Please upgrade Celbridge or correct the version number in the .celbridge file.");
                    return MigrationResult.FromStatus(ProjectMigrationStatus.IncompatibleAppVersion, errorResult);
                }
                
                case VersionComparisonState.UnresolvedVersion:
                {
                    var errorResult = Result.Fail(
                        $"Unable to determine version compatibility. " +
                        $"Project version '{projectVersion}' or application version '{applicationVersion}' is not in a recognized format. " +
                        $"The project will load but Python initialization will be disabled. " +
                        $"Please correct the version number in the .celbridge file and reload the project.");
                    return MigrationResult.FromStatus(ProjectMigrationStatus.InvalidAppVersion, errorResult);
                }
                
                default:
                {
                    var errorResult = Result.Fail($"Unknown version comparison state: {versionState}");
                    return MigrationResult.FromStatus(ProjectMigrationStatus.Failed, errorResult);
                }
            }

            // Perform migration
            _logger.LogInformation($"Starting project migration for: {projectFilePath}");

            // Determine target version based on project version
            // Projects prior to 0.1 should be migrated to 0.1.5 specifically
            string targetVersion;
            try
            {
                var projectVer = new Version(NormalizeVersion(originalProjectVersion));
                var minVersion = new Version("0.1.5");
                
                if (projectVer.CompareTo(minVersion) < 0)
                {
                    // Project is prior to 0.1, migrate to 0.1.5
                    targetVersion = "0.1.5";
                    _logger.LogInformation(
                        "Project version {ProjectVersion} is prior to 0.1 - migrating to version {TargetVersion}",
                        originalProjectVersion,
                        targetVersion);
                }
                else
                {
                    // Project is 0.1 or later, migrate to current application version
                    targetVersion = normalizedAppVersion;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to compare project version for migration");
                var errorResult = Result.Fail($"Failed to determine target version for migration")
                    .WithException(ex);
                return MigrationResult.FromStatus(ProjectMigrationStatus.Failed, errorResult);
            }

            // Todo: Implement version-specific migration logic here

            await Task.CompletedTask;
            
            var writeResult = await WriteApplicationVersionAsync(projectFilePath, originalProjectVersion, targetVersion);
            if (writeResult.IsFailure)
            {
                var errorResult = Result.Fail($"Failed to write Celbridge version to project TOML file: '{projectFilePath}'");
                return MigrationResult.FromStatus(ProjectMigrationStatus.Failed, errorResult);
            }

            _logger.LogInformation("Project migration completed successfully");
            
            return MigrationResult.WithVersions(ProjectMigrationStatus.Success, Result.Ok(), originalProjectVersion, targetVersion);
        }
        catch (Exception ex)
        {
            var errorResult = Result.Fail("An exception occurred during project migration")
                .WithException(ex);
            return MigrationResult.FromStatus(ProjectMigrationStatus.Failed, errorResult);
        }
    }

    /// <summary>
    /// Compare two version strings in the format "major.minor.patch".
    /// Returns a VersionComparisonState indicating the relationship between the versions.
    /// Handles legacy 4-part versions by normalizing them to 3-part format.
    /// The sentinel value "<application-version>" for projectVersion is treated as "use current version" - useful for dev team working with example projects.
    /// </summary>
    private VersionComparisonState CompareVersions(string projectVersion, string applicationVersion)
    {
        // Handle the sentinel value "<application-version>" meaning "use current version"
        // This allows the dev team to work with example projects during development without modifying the version in the file.
        if (projectVersion == ApplicationVersionSentinel)
        {
            _logger.LogInformation("Project version '<application-version>' - using current application version");
            return VersionComparisonState.SameVersion;
        }
        
        // Handle null or whitespace-only project version - we can't safely upgarde in this case.
        if (string.IsNullOrWhiteSpace(projectVersion))
        {
            _logger.LogError("Application version is empty - cannot determine compatibility");
            return VersionComparisonState.UnresolvedVersion;
        }

        // Handle empty/null application version - this should never happen, but fail safely
        if (string.IsNullOrWhiteSpace(applicationVersion))
        {
            _logger.LogError("Application version is empty - cannot determine compatibility");
            return VersionComparisonState.UnresolvedVersion;
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
            return VersionComparisonState.UnresolvedVersion;
        }
        catch (Exception ex)
        {
            // Unexpected error during version comparison
            _logger.LogError(
                ex,
                "Unexpected error comparing versions - ProjectVersion: '{ProjectVersion}', ApplicationVersion: '{ApplicationVersion}'",
                projectVersion,
                applicationVersion);
            return VersionComparisonState.UnresolvedVersion;
        }
    }

    /// <summary>
    /// Normalize a version string to 3-part format (major.minor.patch).
    /// Version must have exactly 3 or 4 parts. If it has 4 parts, the 4th part is discarded with a warning.
    /// Any other format will cause an exception to be thrown.
    /// </summary>
    private string NormalizeVersion(string versionString)
    {
        var parts = versionString.Split('.');
        
        if (parts.Length < 3 || parts.Length > 4)
        {
            throw new ArgumentException(
                $"Version string must have exactly 3 or 4 parts, but '{versionString}' has {parts.Length} parts.");
        }
        
        // Validate the first 3 parts are valid non-negative integers
        if (!int.TryParse(parts[0], out int major) || major < 0 ||
            !int.TryParse(parts[1], out int minor) || minor < 0 ||
            !int.TryParse(parts[2], out int patch) || patch < 0)
        {
            throw new ArgumentException(
                $"Version string '{versionString}' contains invalid numeric parts. All parts of the version number must be non-negative integers.");
        }
        
        // If there's a 4th part, just discard it with a warning
        if (parts.Length == 4)
        {
            _logger.LogWarning(
                "Version '{Version}' uses 4-part format. Converting to 3-part format by discarding revision number.",
                versionString);
        }
        
        return $"{major}.{minor}.{patch}";
    }

    private async Task<Result> WriteApplicationVersionAsync(string projectFilePath, string projectVersion, string applicationVersion)
    {
        try
        {
            var originalText = await File.ReadAllTextAsync(projectFilePath);
            
            // Detect the line ending style used in the original file
            var lineEnding = originalText.Contains("\r\n") ? "\r\n" : "\n";
            
            var updatedText = originalText;
            
            // Remove legacy [celbridge] section format using a regex
            // Matches: [celbridge] line followed by a version = "..." line
            // Pattern: optional indentation, [celbridge], line ending, optional indentation, version = "...", line ending
            var legacyPattern = @"^[ \t]*\[celbridge\][ \t]*\r?\n[ \t]*version[ \t]*=[ \t]*""[^""]*""[ \t]*\r?\n?";
            var legacyMatch = Regex.Match(updatedText, legacyPattern, RegexOptions.Multiline);
            
            bool hasLegacyFormat = legacyMatch.Success;
            
            if (hasLegacyFormat)
            {
                _logger.LogInformation("Removing legacy [celbridge] section from project file");
                updatedText = Regex.Replace(updatedText, legacyPattern, "", RegexOptions.Multiline);
                
                // Insert new version line at the top
                updatedText = $"celbridge.version = \"{applicationVersion}\"{lineEnding}{lineEnding}{updatedText}";
                _logger.LogDebug("Inserted new celbridge.version line at top of file for legacy migration");
            }
            else
            {
                // Update existing celbridge.version line (modern format)
                // Capture leading whitespace to preserve indentation
                var modernPattern = @"^(\s*)celbridge\.version\s*=\s*""[^""]*""";
                var modernMatch = Regex.Match(updatedText, modernPattern, RegexOptions.Multiline);
                
                if (modernMatch.Success)
                {
                    // Preserve the original indentation from capture group 1
                    var leadingWhitespace = modernMatch.Groups[1].Value;
                    updatedText = Regex.Replace(
                        updatedText, 
                        modernPattern, 
                        $"{leadingWhitespace}celbridge.version = \"{applicationVersion}\"",
                        RegexOptions.Multiline);
                    _logger.LogDebug("Updated existing celbridge.version line");
                }
                else
                {
                    // No existing version line found - this should not happen
                    // The version should have been found during parsing in PerformMigrationAsync
                    return Result.Fail("Cannot update version: no existing celbridge.version line found in project file");
                }
            }
            
            // Only write if content actually changed
            if (updatedText != originalText)
            {
                await File.WriteAllTextAsync(projectFilePath, updatedText);
                _logger.LogInformation("Updated project file with application version {ApplicationVersion}", applicationVersion);
            }
            else
            {
                _logger.LogDebug("No changes needed to project file");
            }
            
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail("Failed to write application version to project file")
                .WithException(ex);
        }
    }
}
