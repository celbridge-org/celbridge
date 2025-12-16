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
            var applicationVersion = envInfo.AppVersion;
            
            // Check if migration is needed
            if (string.IsNullOrEmpty(projectVersion))
            {
                _logger.LogInformation("Project has no [celbridge].version - migration required");
            }
            else if (projectVersion == applicationVersion)
            {
                // No migration needed - versions match
                return Result.Ok();
            }
            else
            {
                _logger.LogInformation(
                    "Project migration needed: project version {ProjectVersion}, current version {CurrentVersion}",
                    projectVersion,
                    applicationVersion);
            }

            // Perform migration
            _logger.LogInformation($"Starting project migration for: {projectFilePath}");

            // Todo: Implement version migration logic

            await Task.CompletedTask;

            var writeResult = await WriteApplicationVersionAsync(projectFilePath, projectVersion, applicationVersion);
            if (writeResult.IsFailure)
            {
                return Result.Fail($"Failed to write Celbridge version to project TOML file: '{projectFilePath}'");
            }

            _logger.LogInformation("Project migration completed successfully");
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail("An exception occurred during project migration")
                .WithException(ex);
        }
    }

    private async Task<Result> WriteApplicationVersionAsync(string projectFilePath, string projectVersion, string applicationVersion)
    {
        try
        {
            // 1. Read in the project file text (do not parse it, we're just manipulating text)
            var originalText = await File.ReadAllTextAsync(projectFilePath);
            
            // Detect the line ending style used in the original file
            var lineEnding = originalText.Contains("\r\n") ? "\r\n" : "\n";
            
            // Split by newline and remove any trailing \r characters to handle mixed line endings
            var lines = originalText.Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .ToList();
            
            // 2. Remove any existing celbridge version lines
            var linesToRemove = new List<int>();
            
            for (int i = 0; i < lines.Count; i++)
            {
                var trimmedLine = lines[i].Trim();
                
                // Always remove celbridge.version dotted key format
                if (trimmedLine.StartsWith("celbridge.version"))
                {
                    _logger.LogDebug("Removing existing celbridge.version line at index {Index}", i);
                    linesToRemove.Add(i);
                    continue;
                }
                
                // Remove legacy [celbridge] section format only if version is "0.0.12.0"
                if (projectVersion == "0.0.12.0")
                {
                    if (trimmedLine == "[celbridge]")
                    {
                        _logger.LogDebug("Removing old [celbridge] section header at index {Index}", i);
                        linesToRemove.Add(i);
                        // Also remove the next line if it starts with "version"
                        if (i + 1 < lines.Count && lines[i + 1].Trim().StartsWith("version"))
                        {
                            _logger.LogDebug("Removing old version property at index {Index}", i + 1);
                            linesToRemove.Add(i + 1);
                        }
                    }
                }
            }
            
            // Remove lines in reverse order to maintain indices
            for (int i = linesToRemove.Count - 1; i >= 0; i--)
            {
                lines.RemoveAt(linesToRemove[i]);
            }
            
            if (linesToRemove.Count > 0)
            {
                _logger.LogInformation("Removed {Count} old version line(s) from project file", linesToRemove.Count);
            }
            
            // 3. Insert a new celbridge.version = "{applicationVersion}" line + blank line at the top of the file, after any initial comments
            var insertionIndex = 0;
            
            // Find the position after any initial comments
            for (int i = 0; i < lines.Count; i++)
            {
                var trimmedLine = lines[i].Trim();
                
                // Skip comment lines (starting with #) and empty lines at the beginning
                if (trimmedLine.StartsWith("#") || string.IsNullOrWhiteSpace(trimmedLine))
                {
                    insertionIndex = i + 1;
                }
                else
                {
                    break;
                }
            }
            
            // Insert the new version line followed by a blank line
            lines.Insert(insertionIndex, $"celbridge.version = \"{applicationVersion}\"");
            lines.Insert(insertionIndex + 1, "");
            _logger.LogDebug("Inserted new celbridge.version line at index {Index}", insertionIndex);
            
            // 4. Compare the updated text to the original text - if different, write it back to disk
            var updatedText = string.Join('\n', lines);
            
            if (updatedText != originalText)
            {
                // Remove consecutive blank lines
                var tidiedLines = new List<string>();
                bool previousLineWasBlank = false;
                
                foreach (var line in lines)
                {
                    bool currentLineIsBlank = string.IsNullOrWhiteSpace(line);
                    
                    if (currentLineIsBlank && previousLineWasBlank)
                    {
                        // Skip consecutive blank lines
                        continue;
                    }
                    
                    tidiedLines.Add(line);
                    previousLineWasBlank = currentLineIsBlank;
                }
                
                updatedText = string.Join('\n', tidiedLines);
                
                // Restore the original line ending style
                if (lineEnding == "\r\n")
                {
                    updatedText = updatedText.Replace("\n", "\r\n");
                }
                
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
