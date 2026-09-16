using System.Text.RegularExpressions;
using Celbridge.Platform;
using Celbridge.Logging;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;

namespace Celbridge.Projects.Services;

/// <summary>
/// Result of parsing a project file for version information. Root is the parsed TOML.
/// RecordedCelbridgeVersion is the celbridge-version the project file records, which is the version of
/// Celbridge that last opened the project. CurrentApplicationVersion is the running build's version.
/// </summary>
internal record ProjectVersionInfo(
    TomlTable Root,
    string RecordedCelbridgeVersion,
    string CurrentApplicationVersion);

/// <summary>
/// Reason for a project parse failure.
/// </summary>
internal enum ParseFailureReason
{
    FileNotFound,
    InvalidToml,
    Other
}

/// <summary>
/// Result of attempting to parse project version info.
/// </summary>
internal record ParseResult
{
    public bool IsSuccess { get; init; }
    public ProjectVersionInfo? VersionInfo { get; init; }
    public ParseFailureReason FailureReason { get; init; }
    public Result OperationResult { get; init; } = Result.Ok();

    public static ParseResult Success(ProjectVersionInfo versionInfo) =>
        new() { IsSuccess = true, VersionInfo = versionInfo };

    public static ParseResult Failure(ParseFailureReason reason, Result operationResult) =>
        new() { IsSuccess = false, FailureReason = reason, OperationResult = operationResult };
}

/// <summary>
/// Represents the result of comparing a project's recorded Celbridge version with the current application version.
/// </summary>
internal enum VersionComparisonState
{
    /// <summary>
    /// The recorded Celbridge version matches the current application version - no migration needed.
    /// </summary>
    SameVersion,

    /// <summary>
    /// The recorded Celbridge version is older than the current application version - migration needed.
    /// </summary>
    OlderVersion,

    /// <summary>
    /// The recorded Celbridge version is newer than the current application version - cannot open project.
    /// </summary>
    NewerVersion,

    /// <summary>
    /// The recorded Celbridge version is invalid - cannot open project.
    /// </summary>
    InvalidVersion
}

public class ProjectMigrationService : IProjectMigrationService
{
    private const string ApplicationVersionSentinel = "<application-version>";

    private readonly ILogger<ProjectMigrationService> _logger;
    private readonly IAppEnvironment _environmentService;
    private readonly MigrationStepRegistry _migrationRegistry;
    private readonly ILocalFileSystem _fileSystem;

    public ProjectMigrationService(
        ILogger<ProjectMigrationService> logger,
        IAppEnvironment environmentService,
        IMigrationStepRegistry migrationRegistry,
        ILocalFileSystem fileSystem)
    {
        _logger = logger;
        _environmentService = environmentService;
        _migrationRegistry = (MigrationStepRegistry)migrationRegistry;
        _fileSystem = fileSystem;
        _migrationRegistry.Initialize();
    }

    public async Task<MigrationResult> CheckMigrationAsync(string projectFilePath)
    {
        var parseResult = await ParseProjectVersionInfoAsync(projectFilePath);
        if (!parseResult.IsSuccess)
        {
            var status = parseResult.FailureReason == ParseFailureReason.InvalidToml
                ? MigrationStatus.InvalidConfig
                : MigrationStatus.Failed;
            return MigrationResult.FromStatus(status, parseResult.OperationResult);
        }

        var versionInfo = parseResult.VersionInfo!;
        return ResolveMigrationStatus(versionInfo.RecordedCelbridgeVersion, versionInfo.CurrentApplicationVersion);
    }

    public async Task<MigrationResult> PerformMigrationUpgradeAsync(string projectFilePath)
    {
        var parseResult = await ParseProjectVersionInfoAsync(projectFilePath);
        if (!parseResult.IsSuccess)
        {
            var status = parseResult.FailureReason == ParseFailureReason.InvalidToml
                ? MigrationStatus.InvalidConfig
                : MigrationStatus.Failed;
            return MigrationResult.FromStatus(status, parseResult.OperationResult);
        }

        var versionInfo = parseResult.VersionInfo!;
        return await MigrateProjectAsync(projectFilePath, versionInfo.RecordedCelbridgeVersion, versionInfo.CurrentApplicationVersion, versionInfo.Root);
    }

    private async Task<ParseResult> ParseProjectVersionInfoAsync(string projectFilePath)
    {
        try
        {
            var infoResult = await _fileSystem.GetInfoAsync(projectFilePath);
            if (infoResult.IsFailure || infoResult.Value.Kind != StorageItemKind.File)
            {
                return ParseResult.Failure(
                    ParseFailureReason.FileNotFound,
                    Result.Fail($"Project file does not exist: '{projectFilePath}'"));
            }

            var readResult = await _fileSystem.ReadAllTextAsync(projectFilePath);
            if (readResult.IsFailure)
            {
                return ParseResult.Failure(
                    ParseFailureReason.Other,
                    Result.Fail($"Failed to read project file: '{projectFilePath}'")
                        .WithErrors(readResult));
            }

            // Tomlyn rejects bare-\r line terminators. Normalize once at the
            // gateway boundary so files written by classic-Mac tools (or
            // anything that produced CR-only line endings somewhere upstream)
            // still parse cleanly.
            var text = LineEndingHelper.ConvertLineEndings(readResult.Value, "\n");
            var parse = SyntaxParser.Parse(text);

            if (parse.HasErrors)
            {
                return ParseResult.Failure(
                    ParseFailureReason.InvalidToml,
                    Result.Fail($"Failed to parse project TOML file: {string.Join("; ", parse.Diagnostics)}"));
            }

            var root = TomlSerializer.Deserialize<TomlTable>(text);
            if (root is null)
            {
                return ParseResult.Failure(
                    ParseFailureReason.InvalidToml,
                    Result.Fail("Failed to deserialize project TOML file"));
            }

            // Get the project's Celbridge version from the [celbridge].celbridge-version property
            var recordedCelbridgeVersion = string.Empty;
            if (JsonPointerToml.TryResolve(root, "/celbridge/celbridge-version", out var versionNode, out _) &&
                versionNode is string existingVersion)
            {
                recordedCelbridgeVersion = existingVersion;
            }

            // Get current application version
            var envInfo = _environmentService.GetEnvironmentInfo();
            var currentApplicationVersion = envInfo.AppVersion;

            return ParseResult.Success(new ProjectVersionInfo(root, recordedCelbridgeVersion, currentApplicationVersion));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse project version info");
            return ParseResult.Failure(
                ParseFailureReason.Other,
                Result.Fail("Failed to parse project version info").WithException(ex));
        }
    }

    private MigrationResult ResolveMigrationStatus(string recordedCelbridgeVersion, string currentApplicationVersion)
    {
        // The sentinel value "<application-version>" means "use current version" without updating the file.
        bool usingSentinelVersion = recordedCelbridgeVersion == ApplicationVersionSentinel;

        // Compare versions to determine if migration is needed
        var versionState = CompareVersions(recordedCelbridgeVersion, currentApplicationVersion);

        switch (versionState)
        {
            case VersionComparisonState.SameVersion:
                {
                    // If using the "<application-version>" sentinel value, treat as same version but DO NOT update the project file
                    if (usingSentinelVersion)
                    {
                        _logger.LogInformation(
                            "Celbridge version is sentinel '<application-version>' - treating as current version without updating file: {CurrentVersion}",
                            currentApplicationVersion);

                        // Return the same app version for both old and new to suppress the upgrade notification banner
                        return MigrationResult.WithVersions(MigrationStatus.Complete, Result.Ok(), currentApplicationVersion, currentApplicationVersion);
                    }

                    _logger.LogDebug("Celbridge version matches application version: {Version}", currentApplicationVersion);

                    return MigrationResult.WithVersions(MigrationStatus.Complete, Result.Ok(), currentApplicationVersion, currentApplicationVersion);
                }

            case VersionComparisonState.OlderVersion:
                {
                    // Below the supported floor there are no migration steps to run, so an upgrade would
                    // rewrite the version number and leave the contents untouched. Reject instead, rather
                    // than report a success the project did not get.
                    if (IsBelowMinimumSupportedVersion(recordedCelbridgeVersion))
                    {
                        var errorResult = Result.Fail(
                            $"This project was created with Celbridge v{recordedCelbridgeVersion}, which v{currentApplicationVersion} cannot open. " +
                            $"Projects from before v{ProjectConstants.MinimumSupportedCelbridgeVersion} are not supported. " +
                            $"Open it with the version of Celbridge that created it, or start a new project.");

                        return MigrationResult.FromStatus(MigrationStatus.IncompatibleVersion, errorResult);
                    }

                    _logger.LogInformation(
                        "Project upgrade required: recorded Celbridge version {RecordedCelbridgeVersion}, current application version {CurrentApplicationVersion}",
                        recordedCelbridgeVersion,
                        currentApplicationVersion);

                    // Return UpgradeRequired status - caller must get user confirmation before calling PerformMigrationUpgradeAsync
                    return MigrationResult.WithVersions(MigrationStatus.UpgradeRequired, Result.Ok(), recordedCelbridgeVersion, currentApplicationVersion);
                }

            case VersionComparisonState.NewerVersion:
                {
                    var errorResult = Result.Fail(
                        $"This project was created with a newer version of Celbridge (v{recordedCelbridgeVersion}). " +
                        $"Your current Celbridge version is v{currentApplicationVersion}. " +
                        $"Please upgrade Celbridge or correct the version number in the .celbridge file.");

                    return MigrationResult.FromStatus(MigrationStatus.IncompatibleVersion, errorResult);
                }

            case VersionComparisonState.InvalidVersion:
                {
                    return CreateInvalidVersionResult(recordedCelbridgeVersion, currentApplicationVersion);
                }

            default:
                {
                    var errorResult = Result.Fail($"Unknown version comparison state: {versionState}");
                    return MigrationResult.FromStatus(MigrationStatus.Failed, errorResult);
                }
        }
    }

    private async Task<MigrationResult> MigrateProjectAsync(string projectFilePath, string recordedCelbridgeVersion, string currentApplicationVersion, TomlTable root)
    {
        // Perform migration using step-based approach
        _logger.LogInformation($"Starting project migration for: {projectFilePath}");

        if (!SemanticVersion.TryParse(recordedCelbridgeVersion, out var parsedRecordedVersion) ||
            !SemanticVersion.TryParse(currentApplicationVersion, out var parsedCurrentVersion))
        {
            return CreateInvalidVersionResult(recordedCelbridgeVersion, currentApplicationVersion);
        }

        // Get the list of steps required to migrate from current version to application version
        var requiredSteps = _migrationRegistry.GetRequiredSteps(parsedRecordedVersion, parsedCurrentVersion);

        if (requiredSteps.Count == 0)
        {
            _logger.LogInformation("No migration steps required");

            // We still need to update the version number if it differs
            if (recordedCelbridgeVersion != currentApplicationVersion)
            {
                var writeResult = await WriteCelbridgeVersionAsync(projectFilePath, currentApplicationVersion);
                if (writeResult.IsFailure)
                {
                    var errorResult = Result.Fail($"Failed to write the Celbridge version to project file: '{projectFilePath}'");
                    return MigrationResult.FromStatus(MigrationStatus.Failed, errorResult);
                }
            }

            return MigrationResult.WithVersions(MigrationStatus.Complete, Result.Ok(), recordedCelbridgeVersion, currentApplicationVersion);
        }

        _logger.LogInformation($"Executing {requiredSteps.Count} migration steps");

        // Create migration context
        var projectFolderPath = Path.GetDirectoryName(projectFilePath)!;

        // Local function to write the project file.
        // Line endings are normalized for the current platform.
        Func<string, Task<Result>> writeProjectFileAsync = async (content) =>
        {
            // Normalize line endings to platform standard before writing
            var normalizedContent = LineEndingHelper.ConvertLineEndings(content, LineEndingHelper.PlatformDefault);

            var writeResult = await _fileSystem.WriteAllTextAsync(projectFilePath, normalizedContent);
            if (writeResult.IsFailure)
            {
                return Result.Fail("Failed to write project file")
                    .WithErrors(writeResult);
            }

            return Result.Ok();
        };

        var context = new MigrationContext
        {
            ProjectFilePath = projectFilePath,
            ProjectFolderPath = projectFolderPath,
            Configuration = root,
            Logger = _logger,
            RecordedCelbridgeVersion = parsedRecordedVersion,
            WriteProjectFileAsync = writeProjectFileAsync,
            FileSystem = _fileSystem
        };

        // Execute migration steps in order
        string currentVersion = recordedCelbridgeVersion;
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
            var versionUpdateResult = await WriteCelbridgeVersionAsync(projectFilePath, stepVersionString);
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
        var finalVersion = currentApplicationVersion;
        if (currentVersion != finalVersion)
        {
            var writeResult = await WriteCelbridgeVersionAsync(projectFilePath, finalVersion);
            if (writeResult.IsFailure)
            {
                var errorResult = Result.Fail($"Failed to write the final Celbridge version to project file: '{projectFilePath}'");
                return MigrationResult.FromStatus(MigrationStatus.Failed, errorResult);
            }
        }

        _logger.LogInformation($"Project migration completed successfully: {recordedCelbridgeVersion} >> {finalVersion}");

        return MigrationResult.WithVersions(MigrationStatus.Complete, Result.Ok(), recordedCelbridgeVersion, finalVersion);
    }

    // Only called for a project already known to be older than the application, so the version string has
    // already parsed once. A parse failure here is still treated as below the floor: an unreadable version
    // cannot be shown to be supported.
    private static bool IsBelowMinimumSupportedVersion(string recordedCelbridgeVersion)
    {
        if (!SemanticVersion.TryParse(recordedCelbridgeVersion, out var parsedRecordedVersion) ||
            !SemanticVersion.TryParse(ProjectConstants.MinimumSupportedCelbridgeVersion, out var minimumVersion))
        {
            return true;
        }

        return parsedRecordedVersion < minimumVersion;
    }

    // Compares the project's recorded Celbridge version with the current application version. The <application-version>
    // sentinel counts as the current version.
    private VersionComparisonState CompareVersions(string recordedCelbridgeVersion, string currentApplicationVersion)
    {
        // Handle the sentinel value "<application-version>" meaning "use current version"
        if (recordedCelbridgeVersion == ApplicationVersionSentinel)
        {
            _logger.LogInformation("Celbridge version '<application-version>' - using current application version");
            return VersionComparisonState.SameVersion;
        }

        // Handle null or whitespace-only Celbridge version - we can't safely upgrade in this case.
        if (string.IsNullOrWhiteSpace(recordedCelbridgeVersion))
        {
            _logger.LogError("Celbridge version is empty - cannot determine compatibility");
            return VersionComparisonState.InvalidVersion;
        }

        // Handle empty/null application version - this should never happen, but fail safely
        if (string.IsNullOrWhiteSpace(currentApplicationVersion))
        {
            _logger.LogError("Application version is empty - cannot determine compatibility");
            return VersionComparisonState.InvalidVersion;
        }

        if (!SemanticVersion.TryParse(recordedCelbridgeVersion, out var parsedRecordedVersion) ||
            !SemanticVersion.TryParse(currentApplicationVersion, out var parsedCurrentVersion))
        {
            _logger.LogWarning(
                "Failed to parse version strings - RecordedCelbridgeVersion: '{RecordedCelbridgeVersion}', CurrentApplicationVersion: '{CurrentApplicationVersion}'",
                recordedCelbridgeVersion,
                currentApplicationVersion);
            return VersionComparisonState.InvalidVersion;
        }

        int comparison = parsedRecordedVersion.CompareTo(parsedCurrentVersion);

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

    private static MigrationResult CreateInvalidVersionResult(string recordedCelbridgeVersion, string currentApplicationVersion)
    {
        var errorResult = Result.Fail(
            $"Celbridge version '{recordedCelbridgeVersion}' or application version '{currentApplicationVersion}' is not a three-part version such as {SemanticVersion.Default}. " +
            $"Please correct the celbridge-version value in the .celbridge file and reload the project.");

        return MigrationResult.FromStatus(MigrationStatus.InvalidVersion, errorResult);
    }

    // Writes the given version into the project file's celbridge-version key. The value is the version the
    // project has reached, which during migration is a step's target rather than the current application version.
    private async Task<Result> WriteCelbridgeVersionAsync(string projectFilePath, string version)
    {
        var readResult = await _fileSystem.ReadAllTextAsync(projectFilePath);
        if (readResult.IsFailure)
        {
            return Result.Fail("Failed to read project file when updating the Celbridge version")
                .WithErrors(readResult);
        }

        var originalText = readResult.Value;

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
                $"{leadingWhitespace}celbridge-version = \"{version}\"",
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
            updatedText = LineEndingHelper.ConvertLineEndings(updatedText, LineEndingHelper.PlatformDefault);

            var writeResult = await _fileSystem.WriteAllTextAsync(projectFilePath, updatedText);
            if (writeResult.IsFailure)
            {
                return Result.Fail("Failed to write the Celbridge version to project file")
                    .WithErrors(writeResult);
            }

            _logger.LogInformation("Updated project file with Celbridge version {CelbridgeVersion}", version);
        }

        return Result.Ok();
    }

    private async Task<Result<TomlTable>> ReadProjectConfigAsync(string projectFilePath)
    {
        var readResult = await _fileSystem.ReadAllTextAsync(projectFilePath);
        if (readResult.IsFailure)
        {
            return Result<TomlTable>.Fail("Failed to refresh configuration from project file")
                .WithErrors(readResult);
        }

        // Match ParseProjectVersionInfoAsync: collapse any bare-\r line
        // endings to \n before handing the bytes to Tomlyn.
        var text = LineEndingHelper.ConvertLineEndings(readResult.Value, "\n");
        var parse = SyntaxParser.Parse(text);

        if (parse.HasErrors)
        {
            return Result<TomlTable>.Fail($"Failed to parse project TOML file: {string.Join("; ", parse.Diagnostics)}");
        }

        var root = TomlSerializer.Deserialize<TomlTable>(text);
        if (root is null)
        {
            return Result<TomlTable>.Fail("Failed to deserialize project TOML file");
        }

        return Result<TomlTable>.Ok(root);
    }
}
