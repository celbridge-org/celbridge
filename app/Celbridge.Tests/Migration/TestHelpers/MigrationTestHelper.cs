using Celbridge.Logging;
using Celbridge.Utilities;

namespace Celbridge.Tests.Migration.TestHelpers;

/// <summary>
/// Helper methods for creating mocks and test data for migration tests.
/// </summary>
public static class MigrationTestHelper
{
    /// <summary>
    /// Creates a mock ILogger that doesn't throw on any log operation.
    /// </summary>
    public static ILogger<T> CreateMockLogger<T>()
    {
        return Substitute.For<ILogger<T>>();
    }

    /// <summary>
    /// Creates a mock IUtilityService with a configurable application version.
    /// </summary>
    public static IUtilityService CreateMockUtilityService(string appVersion)
    {
        var mockUtilityService = Substitute.For<IUtilityService>();
        mockUtilityService.GetEnvironmentInfo()
            .Returns(new EnvironmentInfo(appVersion, "Test", "Debug"));
        return mockUtilityService;
    }

    /// <summary>
    /// Creates a temporary TOML project file with the specified version.
    /// </summary>
    public static string CreateTempProjectFile(string version, string? legacyVersion = null)
    {
        var tempPath = Path.GetTempFileName();
        var projectPath = Path.ChangeExtension(tempPath, ".celbridge");
        File.Delete(tempPath);

        string content;
        if (legacyVersion != null)
        {
            // Old format with "version" property (pre-0.1.5)
            content = $"""
                [celbridge]
                version = "{legacyVersion}"

                [project]
                name = "TestProject"
                """;
        }
        else
        {
            // Modern format with "celbridge-version" property
            content = $"""
                [celbridge]
                celbridge-version = "{version}"

                [project]
                name = "TestProject"
                """;
        }

        File.WriteAllText(projectPath, content);
        return projectPath;
    }

    /// <summary>
    /// Creates a temporary TOML project file with invalid TOML syntax.
    /// </summary>
    public static string CreateInvalidTomlFile()
    {
        var tempPath = Path.GetTempFileName();
        var projectPath = Path.ChangeExtension(tempPath, ".celbridge");
        File.Delete(tempPath);

        var content = """
            [celbridge
            celbridge-version = "0.1.5"
            """;

        File.WriteAllText(projectPath, content);
        return projectPath;
    }

    /// <summary>
    /// Reads a project file and returns the celbridge-version value.
    /// </summary>
    public static string? ReadVersionFromFile(string projectFilePath)
    {
        var content = File.ReadAllText(projectFilePath);
        var lines = content.Split('\n');
        
        foreach (var line in lines)
        {
            if (line.Contains("celbridge-version"))
            {
                var parts = line.Split('=');
                if (parts.Length == 2)
                {
                    return parts[1].Trim().Trim('"');
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Cleans up a temporary project file.
    /// </summary>
    public static void CleanupTempFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
