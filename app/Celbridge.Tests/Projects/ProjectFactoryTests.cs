using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Tests.Migration.TestHelpers;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Unit tests for ProjectFactory focusing on project loading and initialization.
/// </summary>
[TestFixture]
public class ProjectFactoryTests
{
    private ILogger<ProjectFactory> _mockLogger = null!;
    private IServiceProvider _mockServiceProvider = null!;
    private ProjectFactory _factory = null!;

    [SetUp]
    public void Setup()
    {
        _mockLogger = MigrationTestHelper.CreateMockLogger<ProjectFactory>();
        
        _mockServiceProvider = Substitute.For<IServiceProvider>();
        _mockServiceProvider.GetService(typeof(IProjectConfigService))
            .Returns(new ProjectConfigService());
        
        _factory = new ProjectFactory(_mockLogger, _mockServiceProvider);
    }

    #region Input Validation Tests

    [Test]
    public async Task LoadAsync_WithEmptyPath_ReturnsFailure()
    {
        // Arrange
        var migrationResult = CreateSuccessfulMigrationResult();

        // Act
        var result = await _factory.LoadAsync(string.Empty, migrationResult);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("empty");
    }

    [Test]
    public async Task LoadAsync_WithNonExistentFile_ReturnsFailure()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "nonexistent_project.celbridge");
        var migrationResult = CreateSuccessfulMigrationResult();

        // Act
        var result = await _factory.LoadAsync(nonExistentPath, migrationResult);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("does not exist");
    }

    #endregion

    #region Successful Load Tests

    [Test]
    public async Task LoadAsync_WithValidFile_ReturnsProject()
    {
        // Arrange
        var projectPath = CreateValidProjectFile();
        var migrationResult = CreateSuccessfulMigrationResult();

        try
        {
            // Act
            var result = await _factory.LoadAsync(projectPath, migrationResult);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value.ProjectFilePath.Should().Be(projectPath);
            result.Value.ProjectName.Should().Be(Path.GetFileNameWithoutExtension(projectPath));
        }
        finally
        {
            CleanupProjectFiles(projectPath);
        }
    }

    [Test]
    public async Task LoadAsync_WithValidFile_CreatesDataFolder()
    {
        // Arrange
        var projectPath = CreateValidProjectFile();
        var migrationResult = CreateSuccessfulMigrationResult();
        var expectedDataFolder = Path.Combine(
            Path.GetDirectoryName(projectPath)!,
            ProjectConstants.MetaDataFolder);

        try
        {
            // Act
            var result = await _factory.LoadAsync(projectPath, migrationResult);

            // Assert
            result.IsSuccess.Should().BeTrue();
            Directory.Exists(expectedDataFolder).Should().BeTrue();
        }
        finally
        {
            CleanupProjectFiles(projectPath);
        }
    }

    [Test]
    public async Task LoadAsync_WithSuccessfulMigration_InitializesConfigService()
    {
        // Arrange
        var projectPath = CreateValidProjectFile();
        var migrationResult = CreateSuccessfulMigrationResult();

        try
        {
            // Act
            var result = await _factory.LoadAsync(projectPath, migrationResult);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.ProjectConfig.Should().NotBeNull();
        }
        finally
        {
            CleanupProjectFiles(projectPath);
        }
    }

    #endregion

    #region Failed Migration Tests

    [Test]
    public async Task LoadAsync_WithFailedMigration_StillCreatesProjectWithMigrationError()
    {
        // Arrange
        var projectPath = CreateValidProjectFile();
        var migrationResult = new MigrationResult(
            Status: MigrationStatus.Failed,
            OldVersion: "0.1.0",
            NewVersion: "1.0.0",
            OperationResult: Result.Fail("Migration failed"));

        try
        {
            // Act
            var result = await _factory.LoadAsync(projectPath, migrationResult);

            // Assert - Project should still be created so UI can show the error
            result.IsSuccess.Should().BeTrue();
            result.Value.MigrationResult.Status.Should().Be(MigrationStatus.Failed);
            result.Value.MigrationResult.OperationResult.IsFailure.Should().BeTrue();
        }
        finally
        {
            CleanupProjectFiles(projectPath);
        }
    }

    #endregion

    #region Helper Methods

    private static MigrationResult CreateSuccessfulMigrationResult()
    {
        return new MigrationResult(
            Status: MigrationStatus.Complete,
            OldVersion: "1.0.0",
            NewVersion: "1.0.0",
            OperationResult: Result.Ok());
    }

    private static string CreateValidProjectFile()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), $"TestProject_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempFolder);
        
        var projectPath = Path.Combine(tempFolder, "test.celbridge");
        var content = """
            [celbridge]
            version = "1.0.0"
            
            [project]
            name = "TestProject"
            """;
        File.WriteAllText(projectPath, content);
        return projectPath;
    }

    private static void CleanupProjectFiles(string projectPath)
    {
        var folder = Path.GetDirectoryName(projectPath);
        if (folder != null && Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    #endregion
}
