using Celbridge.Logging;
using Celbridge.Projects.Services;
using Celbridge.Tests.Migration.TestHelpers;

namespace Celbridge.Tests.Migration;

/// <summary>
/// Unit tests for MigrationStepRegistry which discovers and orders migration steps.
/// </summary>
[TestFixture]
public class MigrationStepRegistryTests
{
    private ILogger<MigrationStepRegistry> _mockLogger = null!;
    private MigrationStepRegistry _registry = null!;

    [SetUp]
    public void Setup()
    {
        _mockLogger = MigrationTestHelper.CreateMockLogger<MigrationStepRegistry>();
        _registry = new MigrationStepRegistry(_mockLogger);
    }

    #region Initialization Tests

    [Test]
    public void Initialize_DiscoversAllMigrationSteps()
    {
        // Act
        _registry.Initialize();

        // Assert - at least MigrationStep_0_1_5 should be discovered
        var steps = _registry.GetRequiredSteps(new Version("0.0.0"), new Version("1.0.0"));
        steps.Should().NotBeEmpty();
        steps.Should().Contain(s => s.TargetVersion == new Version("0.1.5"));
    }

    [Test]
    public void Initialize_CanBeCalledMultipleTimes()
    {
        // Act
        _registry.Initialize();
        _registry.Initialize(); // Should not throw or cause issues

        // Assert
        var steps = _registry.GetRequiredSteps(new Version("0.0.0"), new Version("1.0.0"));
        steps.Should().NotBeEmpty();
    }

    #endregion

    #region GetRequiredSteps Tests

    [Test]
    public void GetRequiredSteps_NoStepsNeeded_ReturnsEmpty()
    {
        // Arrange
        _registry.Initialize();

        // Act
        var steps = _registry.GetRequiredSteps(new Version("1.0.0"), new Version("1.0.0"));

        // Assert
        steps.Should().BeEmpty();
    }

    [Test]
    public void GetRequiredSteps_CurrentVersionNewerThanTarget_ReturnsEmpty()
    {
        // Arrange
        _registry.Initialize();

        // Act
        var steps = _registry.GetRequiredSteps(new Version("2.0.0"), new Version("1.0.0"));

        // Assert
        steps.Should().BeEmpty();
    }

    [Test]
    public void GetRequiredSteps_IncludesStep_0_1_5_WhenInRange()
    {
        // Arrange
        _registry.Initialize();

        // Act
        var steps = _registry.GetRequiredSteps(new Version("0.1.0"), new Version("0.2.0"));

        // Assert
        steps.Should().Contain(s => s.TargetVersion == new Version("0.1.5"));
    }

    [Test]
    public void GetRequiredSteps_ExcludesStepsOutsideRange()
    {
        // Arrange
        _registry.Initialize();

        // Act - request steps from 0.1.6 to 0.2.0 (should exclude 0.1.5)
        var steps = _registry.GetRequiredSteps(new Version("0.1.6"), new Version("0.2.0"));

        // Assert
        steps.Should().NotContain(s => s.TargetVersion == new Version("0.1.5"));
    }

    [Test]
    public void GetRequiredSteps_OrdersStepsByVersion()
    {
        // Arrange
        _registry.Initialize();

        // Act
        var steps = _registry.GetRequiredSteps(new Version("0.0.0"), new Version("1.0.0"));

        // Assert
        steps.Should().NotBeEmpty();
        
        // Verify steps are ordered by version
        for (int i = 1; i < steps.Count; i++)
        {
            steps[i - 1].TargetVersion.Should().BeLessThan(steps[i].TargetVersion,
                "steps should be ordered by target version");
        }
    }

    [Test]
    public void GetRequiredSteps_CurrentVersionExactlyAtStep_ExcludesStep()
    {
        // Arrange
        _registry.Initialize();

        // Act - current version is exactly 0.1.5
        var steps = _registry.GetRequiredSteps(new Version("0.1.5"), new Version("0.2.0"));

        // Assert - should not include 0.1.5 since we're already at that version
        steps.Should().NotContain(s => s.TargetVersion == new Version("0.1.5"));
    }

    [Test]
    public void GetRequiredSteps_TargetVersionExactlyAtStep_IncludesStep()
    {
        // Arrange
        _registry.Initialize();

        // Act - target version is exactly 0.1.5
        var steps = _registry.GetRequiredSteps(new Version("0.1.0"), new Version("0.1.5"));

        // Assert - should include 0.1.5 since that's our target
        steps.Should().Contain(s => s.TargetVersion == new Version("0.1.5"));
    }

    #endregion

    #region Auto-Initialization Tests

    [Test]
    public void GetRequiredSteps_AutoInitializes_WhenNotInitialized()
    {
        // Arrange - don't call Initialize()

        // Act
        var steps = _registry.GetRequiredSteps(new Version("0.0.0"), new Version("1.0.0"));

        // Assert - should still work because GetRequiredSteps calls Initialize() internally
        steps.Should().NotBeEmpty();
        steps.Should().Contain(s => s.TargetVersion == new Version("0.1.5"));
    }

    #endregion

    #region Future Migration Steps Tests

    [Test]
    public void GetRequiredSteps_MultipleStepsInRange_ReturnsAllInOrder()
    {
        // Arrange
        _registry.Initialize();

        // Act - get all steps from beginning to a future version
        var steps = _registry.GetRequiredSteps(new Version("0.0.0"), new Version("10.0.0"));

        // Assert
        steps.Should().NotBeEmpty();
        
        // Verify each step is only included once
        var versions = steps.Select(s => s.TargetVersion).ToList();
        versions.Should().OnlyHaveUniqueItems();
        
        // Verify ordering
        for (int i = 1; i < steps.Count; i++)
        {
            steps[i - 1].TargetVersion.Should().BeLessThan(steps[i].TargetVersion);
        }
    }

    #endregion
}
