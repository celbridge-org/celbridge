using Celbridge.ApplicationEnvironment;
using Celbridge.FileSystem.Services;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Tests.Migration.TestHelpers;

namespace Celbridge.Tests.Migration;

/// <summary>
/// Unit tests for ProjectMigrationService focusing on version comparison and migration resolution logic.
/// </summary>
[TestFixture]
public class ProjectMigrationServiceTests
{
    private ILogger<ProjectMigrationService> _mockLogger = null!;
    private ILogger<MigrationStepRegistry> _mockRegistryLogger = null!;
    private IAppEnvironment _mockEnvironmentService = null!;
    private MigrationStepRegistry _registry = null!;
    private ILocalFileSystem _fileSystem = null!;

    [SetUp]
    public void Setup()
    {
        _mockLogger = MigrationTestHelper.CreateMockLogger<ProjectMigrationService>();
        _mockRegistryLogger = MigrationTestHelper.CreateMockLogger<MigrationStepRegistry>();
        _mockEnvironmentService = MigrationTestHelper.CreateMockEnvironmentService("1.0.0");
        _registry = new MigrationStepRegistry(_mockRegistryLogger);
        _fileSystem = new LocalFileSystem(MigrationTestHelper.CreateMockLogger<LocalFileSystem>());
    }

    #region File Validation Tests

    [Test]
    public async Task CheckMigrationAsync_NonExistentFile_ReturnsFailedStatus()
    {
        // Arrange
        var service = new ProjectMigrationService(_mockLogger, _mockEnvironmentService, _registry, _fileSystem);
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "nonexistent.celbridge");

        // Act
        var result = await service.CheckMigrationAsync(nonExistentPath);

        // Assert
        result.Status.Should().Be(MigrationStatus.Failed);
        result.OperationResult.IsFailure.Should().BeTrue();
        result.OperationResult.DiagnosticReport.Should().Contain("does not exist");
    }

    [Test]
    public async Task CheckMigrationAsync_InvalidToml_ReturnsInvalidConfig()
    {
        // Arrange
        var service = new ProjectMigrationService(_mockLogger, _mockEnvironmentService, _registry, _fileSystem);
        var projectPath = MigrationTestHelper.CreateInvalidTomlFile();

        try
        {
            // Act
            var result = await service.CheckMigrationAsync(projectPath);

            // Assert
            result.Status.Should().Be(MigrationStatus.InvalidConfig);
            result.OperationResult.IsFailure.Should().BeTrue();
        }
        finally
        {
            MigrationTestHelper.CleanupTempFile(projectPath);
        }
    }

    [Test]
    public async Task CheckMigrationAsync_BareCarriageReturnLineEndings_ParsesSuccessfully()
    {
        // Regression: a project file written with bare-\r line endings (classic
        // Mac style, or whatever upstream tool ends up producing CR-only output)
        // used to fail parsing in Tomlyn with "Invalid \r not followed by \n".
        // The migration service normalises line endings before handing the text
        // to Tomlyn so any of the three conventions parse cleanly.
        var appVersion = "1.0.0";
        _mockEnvironmentService = MigrationTestHelper.CreateMockEnvironmentService(appVersion);
        var service = new ProjectMigrationService(_mockLogger, _mockEnvironmentService, _registry, _fileSystem);

        var tempPath = Path.GetTempFileName();
        var projectPath = Path.ChangeExtension(tempPath, ".celbridge");
        File.Delete(tempPath);

        var bareCrContent = $"[celbridge]\rcelbridge-version = \"{appVersion}\"\r\r[project]\rname = \"TestProject\"\r";
        File.WriteAllText(projectPath, bareCrContent);

        try
        {
            var result = await service.CheckMigrationAsync(projectPath);

            result.Status.Should().Be(MigrationStatus.Complete);
            result.OperationResult.IsSuccess.Should().BeTrue();
            result.OldVersion.Should().Be(appVersion);
        }
        finally
        {
            MigrationTestHelper.CleanupTempFile(projectPath);
        }
    }

    #endregion

    #region Same Version Tests

    [Test]
    public async Task CheckMigrationAsync_SameVersion_ReturnsComplete()
    {
        // Arrange
        var appVersion = "1.0.0";
        _mockEnvironmentService = MigrationTestHelper.CreateMockEnvironmentService(appVersion);
        var service = new ProjectMigrationService(_mockLogger, _mockEnvironmentService, _registry, _fileSystem);
        var projectPath = MigrationTestHelper.CreateTempProjectFile(appVersion);

        try
        {
            // Act
            var result = await service.CheckMigrationAsync(projectPath);

            // Assert
            result.Status.Should().Be(MigrationStatus.Complete);
            result.OperationResult.IsSuccess.Should().BeTrue();
            result.OldVersion.Should().Be(appVersion);
            result.NewVersion.Should().Be(appVersion);
        }
        finally
        {
            MigrationTestHelper.CleanupTempFile(projectPath);
        }
    }

    [Test]
    public async Task CheckMigrationAsync_SentinelVersion_ReturnsComplete_DoesNotModifyFile()
    {
        // Arrange
        var appVersion = "1.0.0";
        _mockEnvironmentService = MigrationTestHelper.CreateMockEnvironmentService(appVersion);
        var service = new ProjectMigrationService(_mockLogger, _mockEnvironmentService, _registry, _fileSystem);
        var projectPath = MigrationTestHelper.CreateTempProjectFile("<application-version>");

        try
        {
            var originalContent = File.ReadAllText(projectPath);

            // Act
            var result = await service.CheckMigrationAsync(projectPath);

            // Assert
            result.Status.Should().Be(MigrationStatus.Complete);
            result.OperationResult.IsSuccess.Should().BeTrue();
            result.OldVersion.Should().Be(appVersion);
            result.NewVersion.Should().Be(appVersion);

            // Verify file was not modified
            var newContent = File.ReadAllText(projectPath);
            newContent.Should().Be(originalContent);
            newContent.Should().Contain("<application-version>");
        }
        finally
        {
            MigrationTestHelper.CleanupTempFile(projectPath);
        }
    }

    #endregion

    #region Newer Version Tests

    [Test]
    public async Task CheckMigrationAsync_NewerProjectVersion_ReturnsIncompatibleVersion()
    {
        // Arrange
        var appVersion = "1.0.0";
        var projectVersion = "2.0.0";
        _mockEnvironmentService = MigrationTestHelper.CreateMockEnvironmentService(appVersion);
        var service = new ProjectMigrationService(_mockLogger, _mockEnvironmentService, _registry, _fileSystem);
        var projectPath = MigrationTestHelper.CreateTempProjectFile(projectVersion);

        try
        {
            // Act
            var result = await service.CheckMigrationAsync(projectPath);

            // Assert
            result.Status.Should().Be(MigrationStatus.IncompatibleVersion);
            result.OperationResult.IsFailure.Should().BeTrue();
            result.OperationResult.DiagnosticReport.Should().Contain("newer version");
        }
        finally
        {
            MigrationTestHelper.CleanupTempFile(projectPath);
        }
    }

    #endregion

    #region Invalid Version Tests

    [Test]
    public async Task CheckMigrationAsync_EmptyProjectVersion_ReturnsInvalidVersion()
    {
        // Arrange
        _mockEnvironmentService = MigrationTestHelper.CreateMockEnvironmentService("1.0.0");
        var service = new ProjectMigrationService(_mockLogger, _mockEnvironmentService, _registry, _fileSystem);
        var projectPath = MigrationTestHelper.CreateTempProjectFile("");

        try
        {
            // Act
            var result = await service.CheckMigrationAsync(projectPath);

            // Assert
            result.Status.Should().Be(MigrationStatus.InvalidVersion);
            result.OperationResult.IsFailure.Should().BeTrue();
        }
        finally
        {
            MigrationTestHelper.CleanupTempFile(projectPath);
        }
    }

    [Test]
    public async Task CheckMigrationAsync_InvalidVersionFormat_ReturnsInvalidVersion()
    {
        // Arrange
        _mockEnvironmentService = MigrationTestHelper.CreateMockEnvironmentService("1.0.0");
        var service = new ProjectMigrationService(_mockLogger, _mockEnvironmentService, _registry, _fileSystem);
        var projectPath = MigrationTestHelper.CreateTempProjectFile("not.a.version");

        try
        {
            // Act
            var result = await service.CheckMigrationAsync(projectPath);

            // Assert
            result.Status.Should().Be(MigrationStatus.InvalidVersion);
            result.OperationResult.IsFailure.Should().BeTrue();
        }
        finally
        {
            MigrationTestHelper.CleanupTempFile(projectPath);
        }
    }

    #endregion

    #region Version Update Tests

    [Test]
    public async Task CheckMigrationAsync_OlderVersion_ReturnsUpgradeRequired()
    {
        // Arrange
        var appVersion = "1.0.1";
        var projectVersion = "1.0.0";
        _mockEnvironmentService = MigrationTestHelper.CreateMockEnvironmentService(appVersion);
        var service = new ProjectMigrationService(_mockLogger, _mockEnvironmentService, _registry, _fileSystem);
        var projectPath = MigrationTestHelper.CreateTempProjectFile(projectVersion);

        try
        {
            // Act
            var result = await service.CheckMigrationAsync(projectPath);

            // Assert - Should return UpgradeRequired
            result.Status.Should().Be(MigrationStatus.UpgradeRequired);
            result.OperationResult.IsSuccess.Should().BeTrue();
            result.OldVersion.Should().Be(projectVersion);
            result.NewVersion.Should().Be(appVersion);

            // Verify file was NOT updated (only checking, not upgrading)
            var updatedVersion = MigrationTestHelper.ReadVersionFromFile(projectPath);
            updatedVersion.Should().Be(projectVersion);
        }
        finally
        {
            MigrationTestHelper.CleanupTempFile(projectPath);
        }
    }

    [Test]
    public async Task PerformMigrationUpgradeAsync_OlderVersion_NoSteps_UpdatesVersion()
    {
        // Arrange
        var appVersion = "1.0.1";
        var projectVersion = "1.0.0";
        _mockEnvironmentService = MigrationTestHelper.CreateMockEnvironmentService(appVersion);
        var service = new ProjectMigrationService(_mockLogger, _mockEnvironmentService, _registry, _fileSystem);
        var projectPath = MigrationTestHelper.CreateTempProjectFile(projectVersion);

        try
        {
            // Act
            var result = await service.PerformMigrationUpgradeAsync(projectPath);

            // Assert
            result.Status.Should().Be(MigrationStatus.Complete);
            result.OperationResult.IsSuccess.Should().BeTrue();
            result.OldVersion.Should().Be(projectVersion);
            result.NewVersion.Should().Be(appVersion);

            // Verify file was updated
            var updatedVersion = MigrationTestHelper.ReadVersionFromFile(projectPath);
            updatedVersion.Should().Be(appVersion);
        }
        finally
        {
            MigrationTestHelper.CleanupTempFile(projectPath);
        }
    }

    #endregion

    #region Migration Step Execution Tests

    [Test]
    public async Task PerformMigrationUpgradeAsync_RunsApplicableStep_AndUpdatesVersion()
    {
        // A project a version behind the app runs the matching migration step
        // during the upgrade. The 0.2.0 step converts a legacy nested
        // [shortcuts...] table into the [[shortcut]] array format, and the
        // version is stamped to the application version once steps complete.
        var appVersion = "0.2.0";
        var projectVersion = "0.1.6";
        _mockEnvironmentService = MigrationTestHelper.CreateMockEnvironmentService(appVersion);

        // Real registry so the upgrade discovers the 0.2.0 step.
        var registry = new MigrationStepRegistry(_mockRegistryLogger);
        registry.Initialize();

        var service = new ProjectMigrationService(_mockLogger, _mockEnvironmentService, registry, _fileSystem);

        var tempPath = Path.GetTempFileName();
        var projectPath = Path.ChangeExtension(tempPath, ".celbridge");
        File.Delete(tempPath);

        var content = """
            [celbridge]
            celbridge-version = "0.1.6"

            [project]
            name = "TestProject"

            [shortcuts.navigation_bar.run_examples]
            icon = "Play"
            tooltip = "Run examples"
            """;
        File.WriteAllText(projectPath, content);

        try
        {
            // Act
            var result = await service.PerformMigrationUpgradeAsync(projectPath);

            // Assert
            result.Status.Should().Be(MigrationStatus.Complete);
            result.OperationResult.IsSuccess.Should().BeTrue();
            result.OldVersion.Should().Be(projectVersion);
            result.NewVersion.Should().Be(appVersion);

            // The 0.2.0 step rewrote the legacy shortcuts table into the new
            // [[shortcut]] array format.
            var migratedContent = File.ReadAllText(projectPath);
            migratedContent.Should().Contain("[[shortcut]]");
            migratedContent.Should().NotContain("[shortcuts.navigation_bar");

            var updatedVersion = MigrationTestHelper.ReadVersionFromFile(projectPath);
            updatedVersion.Should().Be(appVersion);
        }
        finally
        {
            MigrationTestHelper.CleanupTempFile(projectPath);
        }
    }

    #endregion
}








