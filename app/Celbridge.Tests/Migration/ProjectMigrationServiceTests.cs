using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Tests.Migration.TestHelpers;
using Celbridge.Utilities;

namespace Celbridge.Tests.Migration;

/// <summary>
/// Unit tests for ProjectMigrationService focusing on version comparison and migration resolution logic.
/// </summary>
[TestFixture]
public class ProjectMigrationServiceTests
{
    private ILogger<ProjectMigrationService> _mockLogger = null!;
    private ILogger<MigrationStepRegistry> _mockRegistryLogger = null!;
    private IUtilityService _mockUtilityService = null!;
    private MigrationStepRegistry _registry = null!;

    [SetUp]
    public void Setup()
    {
        _mockLogger = MigrationTestHelper.CreateMockLogger<ProjectMigrationService>();
        _mockRegistryLogger = MigrationTestHelper.CreateMockLogger<MigrationStepRegistry>();
        _mockUtilityService = MigrationTestHelper.CreateMockUtilityService("1.0.0");
        _registry = new MigrationStepRegistry(_mockRegistryLogger);
    }

    #region File Validation Tests

    [Test]
    public async Task CheckMigrationAsync_NonExistentFile_ReturnsFailedStatus()
    {
        // Arrange
        var service = new ProjectMigrationService(_mockLogger, _mockUtilityService, _registry);
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "nonexistent.celbridge");

        // Act
        var result = await service.CheckMigrationAsync(nonExistentPath);

        // Assert
        result.Status.Should().Be(MigrationStatus.Failed);
        result.OperationResult.IsFailure.Should().BeTrue();
        result.OperationResult.Error.Should().Contain("does not exist");
    }

    [Test]
    public async Task CheckMigrationAsync_InvalidToml_ReturnsInvalidConfig()
    {
        // Arrange
        var service = new ProjectMigrationService(_mockLogger, _mockUtilityService, _registry);
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

    #endregion

    #region Same Version Tests

    [Test]
    public async Task CheckMigrationAsync_SameVersion_ReturnsComplete()
    {
        // Arrange
        var appVersion = "1.0.0";
        _mockUtilityService = MigrationTestHelper.CreateMockUtilityService(appVersion);
        var service = new ProjectMigrationService(_mockLogger, _mockUtilityService, _registry);
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
        _mockUtilityService = MigrationTestHelper.CreateMockUtilityService(appVersion);
        var service = new ProjectMigrationService(_mockLogger, _mockUtilityService, _registry);
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
        _mockUtilityService = MigrationTestHelper.CreateMockUtilityService(appVersion);
        var service = new ProjectMigrationService(_mockLogger, _mockUtilityService, _registry);
        var projectPath = MigrationTestHelper.CreateTempProjectFile(projectVersion);

        try
        {
            // Act
            var result = await service.CheckMigrationAsync(projectPath);

            // Assert
            result.Status.Should().Be(MigrationStatus.IncompatibleVersion);
            result.OperationResult.IsFailure.Should().BeTrue();
            result.OperationResult.Error.Should().Contain("newer version");
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
        _mockUtilityService = MigrationTestHelper.CreateMockUtilityService("1.0.0");
        var service = new ProjectMigrationService(_mockLogger, _mockUtilityService, _registry);
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
        _mockUtilityService = MigrationTestHelper.CreateMockUtilityService("1.0.0");
        var service = new ProjectMigrationService(_mockLogger, _mockUtilityService, _registry);
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

    #region Legacy Version Compatibility Tests

    [Test]
    public async Task CheckMigrationAsync_LegacyVersionFormat_ReturnsUpgradeRequired()
    {
        // Arrange
        var appVersion = "0.1.5";
        _mockUtilityService = MigrationTestHelper.CreateMockUtilityService(appVersion);
        var service = new ProjectMigrationService(_mockLogger, _mockUtilityService, _registry);
        
        // Create file with legacy "version" property (pre-0.1.5)
        var projectPath = MigrationTestHelper.CreateTempProjectFile("", legacyVersion: "0.1.4");

        try
        {
            // Act
            var result = await service.CheckMigrationAsync(projectPath);

            // Assert - Should return UpgradeRequired, not Complete
            result.Status.Should().Be(MigrationStatus.UpgradeRequired);
            result.OldVersion.Should().Be("0.1.4");
            result.NewVersion.Should().Be(appVersion);
        }
        finally
        {
            MigrationTestHelper.CleanupTempFile(projectPath);
        }
    }

    [Test]
    public async Task PerformMigrationUpgradeAsync_LegacyVersionFormat_PerformsMigration()
    {
        // Arrange
        var appVersion = "0.1.5";
        _mockUtilityService = MigrationTestHelper.CreateMockUtilityService(appVersion);
        
        // Use real registry which will discover MigrationStep_0_1_5
        var registry = new MigrationStepRegistry(_mockRegistryLogger);
        registry.Initialize();
        
        var service = new ProjectMigrationService(_mockLogger, _mockUtilityService, registry);
        
        // Create file with legacy "version" property (pre-0.1.5)
        var projectPath = MigrationTestHelper.CreateTempProjectFile("", legacyVersion: "0.1.4");

        try
        {
            // Act
            var result = await service.PerformMigrationUpgradeAsync(projectPath);

            // Assert
            result.Status.Should().Be(MigrationStatus.Complete);
            result.OldVersion.Should().Be("0.1.4");
            result.NewVersion.Should().Be(appVersion);
        }
        finally
        {
            MigrationTestHelper.CleanupTempFile(projectPath);
        }
    }

    [Test]
    public async Task CheckMigrationAsync_LegacyVersion4Part_ReturnsUpgradeRequired()
    {
        // Arrange
        var appVersion = "0.1.5";
        _mockUtilityService = MigrationTestHelper.CreateMockUtilityService(appVersion);
        var service = new ProjectMigrationService(_mockLogger, _mockUtilityService, _registry);
        
        // Create file with legacy 4-part version
        var projectPath = MigrationTestHelper.CreateTempProjectFile("", legacyVersion: "0.1.4.2");

        try
        {
            // Act
            var result = await service.CheckMigrationAsync(projectPath);

            // Assert - should return UpgradeRequired
            result.Status.Should().Be(MigrationStatus.UpgradeRequired);
            result.OldVersion.Should().Be("0.1.4.2");
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
        _mockUtilityService = MigrationTestHelper.CreateMockUtilityService(appVersion);
        var service = new ProjectMigrationService(_mockLogger, _mockUtilityService, _registry);
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
        _mockUtilityService = MigrationTestHelper.CreateMockUtilityService(appVersion);
        var service = new ProjectMigrationService(_mockLogger, _mockUtilityService, _registry);
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
    public async Task PerformMigrationUpgradeAsync_WithMigrationSteps_ExecutesStepsInOrder()
    {
        // Arrange
        var appVersion = "0.2.0";
        var projectVersion = "0.1.4";
        _mockUtilityService = MigrationTestHelper.CreateMockUtilityService(appVersion);
        
        // Use real registry which will discover MigrationStep_0_1_5
        var registry = new MigrationStepRegistry(_mockRegistryLogger);
        registry.Initialize();
        
        var service = new ProjectMigrationService(_mockLogger, _mockUtilityService, registry);
        var projectPath = MigrationTestHelper.CreateTempProjectFile("", legacyVersion: projectVersion);

        try
        {
            // Act
            var result = await service.PerformMigrationUpgradeAsync(projectPath);

            // Assert
            result.Status.Should().Be(MigrationStatus.Complete);
            result.OperationResult.IsSuccess.Should().BeTrue();
            result.OldVersion.Should().Be(projectVersion);
            result.NewVersion.Should().Be(appVersion);

            // Verify file was migrated to new format
            var content = File.ReadAllText(projectPath);
            content.Should().Contain("celbridge-version");
            content.Should().NotContain("\r\nversion = ");
            
            var updatedVersion = MigrationTestHelper.ReadVersionFromFile(projectPath);
            updatedVersion.Should().Be(appVersion);
        }
        finally
        {
            MigrationTestHelper.CleanupTempFile(projectPath);
        }
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task CheckMigrationAsync_Exception_ReturnsFailedStatus()
    {
        // Arrange
        var mockUtilityService = Substitute.For<IUtilityService>();
        mockUtilityService.GetEnvironmentInfo()
            .Returns(x => throw new InvalidOperationException("Test exception"));
        
        var service = new ProjectMigrationService(_mockLogger, mockUtilityService, _registry);
        var projectPath = MigrationTestHelper.CreateTempProjectFile("1.0.0");

        try
        {
            // Act
            var result = await service.CheckMigrationAsync(projectPath);

            // Assert
            result.Status.Should().Be(MigrationStatus.Failed);
            result.OperationResult.IsFailure.Should().BeTrue();
        }
        finally
        {
            MigrationTestHelper.CleanupTempFile(projectPath);
        }
    }

    #endregion
}
