using Celbridge.Logging;
using Tomlyn;
using Tomlyn.Model;

namespace Celbridge.Projects.Services;

/// <summary>
/// Metadata about a project configuration file, without fully loading the project.
/// </summary>
public record ProjectMetadata(
    string ProjectFilePath,
    string ProjectName,
    string ProjectFolderPath,
    string? CelbridgeVersion,
    bool IsConfigValid);

/// <summary>
/// Reads project metadata from a .celbridge file without creating a Project instance or modifying state.
/// </summary>
public class ProjectConfigReader
{
    private readonly ILogger<ProjectConfigReader> _logger;
    private readonly ILocalFileSystem _fileSystem;

    public ProjectConfigReader(
        ILogger<ProjectConfigReader> logger,
        ILocalFileSystem fileSystem)
    {
        _logger = logger;
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Reads project metadata from a .celbridge file.
    /// Returns partial metadata even if TOML parsing fails (for error reporting).
    /// </summary>
    public Result<ProjectMetadata> ReadProjectMetadata(string projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
        {
            return Result<ProjectMetadata>.Fail("Project file path is empty");
        }

        // Probe and read synchronously through the gateway to preserve the
        // existing sync caller contract.
        var infoResult = SyncRunner.Run(() => _fileSystem.GetInfoAsync(projectFilePath));
        if (infoResult.IsFailure || infoResult.Value.Kind != StorageItemKind.File)
        {
            return Result<ProjectMetadata>.Fail($"Project file does not exist: '{projectFilePath}'");
        }

        var projectName = System.IO.Path.GetFileNameWithoutExtension(projectFilePath);
        var projectFolderPath = System.IO.Path.GetDirectoryName(projectFilePath) ?? string.Empty;

        string? celbridgeVersion = null;
        bool isConfigValid = false;

        try
        {
            var readResult = SyncRunner.Run(() => _fileSystem.ReadAllTextAsync(projectFilePath));
            if (readResult.IsFailure)
            {
                _logger.LogWarning($"Failed to read project file '{projectFilePath}': {readResult.DiagnosticReport}");

                return Result<ProjectMetadata>.Ok(new ProjectMetadata(
                    projectFilePath,
                    projectName,
                    projectFolderPath,
                    CelbridgeVersion: null,
                    IsConfigValid: false));
            }

            var text = readResult.Value;
            var parse = Toml.Parse(text);

            if (parse.HasErrors)
            {
                var errors = string.Join("; ", parse.Diagnostics.Select(d => d.ToString()));
                _logger.LogWarning($"TOML parse error(s) in '{projectFilePath}': {errors}");
                
                // Return partial metadata with IsConfigValid = false
                return Result<ProjectMetadata>.Ok(new ProjectMetadata(
                    projectFilePath,
                    projectName,
                    projectFolderPath,
                    CelbridgeVersion: null,
                    IsConfigValid: false));
            }

            var root = (TomlTable)parse.ToModel();
            isConfigValid = true;

            // Try to extract version from [celbridge] section
            if (root.TryGetValue("celbridge", out var celbridgeObj) && celbridgeObj is TomlTable celbridgeTable)
            {
                if (celbridgeTable.TryGetValue("version", out var versionObj) && versionObj is string version)
                {
                    celbridgeVersion = version;
                }
            }

            return Result<ProjectMetadata>.Ok(new ProjectMetadata(
                projectFilePath,
                projectName,
                projectFolderPath,
                celbridgeVersion,
                isConfigValid));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Exception reading project metadata from '{projectFilePath}'");
            
            // Return partial metadata with IsConfigValid = false
            return Result<ProjectMetadata>.Ok(new ProjectMetadata(
                projectFilePath,
                projectName,
                projectFolderPath,
                CelbridgeVersion: null,
                IsConfigValid: false));
        }
    }
}
