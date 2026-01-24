using Celbridge.Logging;
using Celbridge.Projects.Services;
using Celbridge.Tests.Migration.TestHelpers;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Unit tests for ProjectConfigReader focusing on metadata extraction and error handling.
/// </summary>
[TestFixture]
public class ProjectConfigReaderTests
{
    private ILogger<ProjectConfigReader> _mockLogger = null!;
    private ProjectConfigReader _reader = null!;

    [SetUp]
    public void Setup()
    {
        _mockLogger = MigrationTestHelper.CreateMockLogger<ProjectConfigReader>();
        _reader = new ProjectConfigReader(_mockLogger);
    }

    #region Input Validation Tests

    [Test]
    public void ReadProjectMetadata_WithEmptyPath_ReturnsFailure()
    {
        // Act
        var result = _reader.ReadProjectMetadata(string.Empty);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("empty");
    }

    [Test]
    public void ReadProjectMetadata_WithNullPath_ReturnsFailure()
    {
        // Act
        var result = _reader.ReadProjectMetadata(null!);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("empty");
    }

    [Test]
    public void ReadProjectMetadata_WithNonExistentFile_ReturnsFailureWithPath()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "nonexistent_project.celbridge");

        // Act
        var result = _reader.ReadProjectMetadata(nonExistentPath);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("does not exist");
        result.Error.Should().Contain(nonExistentPath);
    }

    #endregion

    #region Malformed TOML Tests

    [Test]
    public void ReadProjectMetadata_WithMalformedToml_ReturnsPartialMetadataWithInvalidFlag()
    {
        // Arrange
        var projectPath = CreateTempProjectFileWithContent("this is not { valid toml }}}");

        try
        {
            // Act
            var result = _reader.ReadProjectMetadata(projectPath);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var metadata = result.Value;
            metadata.IsConfigValid.Should().BeFalse();
            metadata.ProjectFilePath.Should().Be(projectPath);
            metadata.ProjectName.Should().Be(Path.GetFileNameWithoutExtension(projectPath));
            metadata.CelbridgeVersion.Should().BeNull();
        }
        finally
        {
            CleanupTempFile(projectPath);
        }
    }

    #endregion

    #region Version Extraction Tests

    [Test]
    public void ReadProjectMetadata_WithMissingVersionField_ReturnsNullVersion()
    {
        // Arrange - valid TOML but no version field
        var content = """
            [celbridge]
            # version field is missing
            
            [project]
            name = "TestProject"
            """;
        var projectPath = CreateTempProjectFileWithContent(content);

        try
        {
            // Act
            var result = _reader.ReadProjectMetadata(projectPath);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var metadata = result.Value;
            metadata.IsConfigValid.Should().BeTrue();
            metadata.CelbridgeVersion.Should().BeNull();
        }
        finally
        {
            CleanupTempFile(projectPath);
        }
    }

    [Test]
    public void ReadProjectMetadata_WithMissingCelbridgeSection_ReturnsNullVersion()
    {
        // Arrange - valid TOML but no [celbridge] section
        var content = """
            [project]
            name = "TestProject"
            """;
        var projectPath = CreateTempProjectFileWithContent(content);

        try
        {
            // Act
            var result = _reader.ReadProjectMetadata(projectPath);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var metadata = result.Value;
            metadata.IsConfigValid.Should().BeTrue();
            metadata.CelbridgeVersion.Should().BeNull();
        }
        finally
        {
            CleanupTempFile(projectPath);
        }
    }

    [Test]
    public void ReadProjectMetadata_WithValidVersion_ReturnsVersion()
    {
        // Arrange
        var expectedVersion = "1.2.3";
        var content = $"""
            [celbridge]
            version = "{expectedVersion}"
            
            [project]
            name = "TestProject"
            """;
        var projectPath = CreateTempProjectFileWithContent(content);

        try
        {
            // Act
            var result = _reader.ReadProjectMetadata(projectPath);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var metadata = result.Value;
            metadata.IsConfigValid.Should().BeTrue();
            metadata.CelbridgeVersion.Should().Be(expectedVersion);
        }
        finally
        {
            CleanupTempFile(projectPath);
        }
    }

    #endregion

    #region Path Extraction Tests

    [Test]
    public void ReadProjectMetadata_ExtractsCorrectPaths()
    {
        // Arrange
        var content = """
            [celbridge]
            version = "1.0.0"
            """;
        var projectPath = CreateTempProjectFileWithContent(content);

        try
        {
            // Act
            var result = _reader.ReadProjectMetadata(projectPath);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var metadata = result.Value;
            metadata.ProjectFilePath.Should().Be(projectPath);
            metadata.ProjectName.Should().Be(Path.GetFileNameWithoutExtension(projectPath));
            metadata.ProjectFolderPath.Should().Be(Path.GetDirectoryName(projectPath));
        }
        finally
        {
            CleanupTempFile(projectPath);
        }
    }

    #endregion

    #region Helper Methods

    private static string CreateTempProjectFileWithContent(string content)
    {
        var tempPath = Path.GetTempFileName();
        var projectPath = Path.ChangeExtension(tempPath, ".celbridge");
        File.Delete(tempPath);
        File.WriteAllText(projectPath, content);
        return projectPath;
    }

    private static void CleanupTempFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    #endregion
}
