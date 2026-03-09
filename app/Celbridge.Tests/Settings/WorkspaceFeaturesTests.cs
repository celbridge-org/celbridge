using Celbridge.Projects;
using Celbridge.Settings;
using Celbridge.Workspace.Services;

namespace Celbridge.Tests.Settings;

/// <summary>
/// Unit tests for WorkspaceFeatures focusing on feature flag precedence and fallback behavior.
/// </summary>
[TestFixture]
public class WorkspaceFeaturesTests
{
    private IProjectService _mockProjectService = null!;
    private IFeatureFlagService _mockFeatureFlagService = null!;
    private WorkspaceFeatures _workspaceFeatures = null!;

    [SetUp]
    public void Setup()
    {
        _mockProjectService = Substitute.For<IProjectService>();
        _mockFeatureFlagService = Substitute.For<IFeatureFlagService>();
        _workspaceFeatures = new WorkspaceFeatures(_mockProjectService, _mockFeatureFlagService);
    }

    #region Precedence Tests

    [Test]
    public void IsEnabled_WorkspaceOverridesAppLevel_ReturnsWorkspaceValue()
    {
        // Arrange - Workspace enables feature, app disables it
        var config = new ProjectConfig
        {
            Features = new Dictionary<string, bool>
            {
                ["notes-editor"] = true
            }
        };
        var mockProject = CreateMockProject(config);
        _mockProjectService.CurrentProject.Returns(mockProject);
        _mockFeatureFlagService.IsEnabled("notes-editor").Returns(false);

        // Act
        var result = _workspaceFeatures.IsEnabled("notes-editor");

        // Assert
        result.Should().BeTrue("workspace feature should override app-level feature");
    }

    [Test]
    public void IsEnabled_WorkspaceDisablesFeature_OverridesAppLevel()
    {
        // Arrange - Workspace disables feature, app enables it
        var config = new ProjectConfig
        {
            Features = new Dictionary<string, bool>
            {
                ["console"] = false
            }
        };
        var mockProject = CreateMockProject(config);
        _mockProjectService.CurrentProject.Returns(mockProject);
        _mockFeatureFlagService.IsEnabled("console").Returns(true);

        // Act
        var result = _workspaceFeatures.IsEnabled("console");

        // Assert
        result.Should().BeFalse("workspace feature should override app-level feature");
    }

    #endregion

    #region Fallback Tests

    [Test]
    public void IsEnabled_FeatureNotInWorkspace_FallsBackToAppLevel()
    {
        // Arrange - Feature not specified in workspace
        var config = new ProjectConfig
        {
            Features = new Dictionary<string, bool>
            {
                ["notes-editor"] = true
            }
        };
        var mockProject = CreateMockProject(config);
        _mockProjectService.CurrentProject.Returns(mockProject);
        _mockFeatureFlagService.IsEnabled("console").Returns(true);

        // Act
        var result = _workspaceFeatures.IsEnabled("console");

        // Assert
        result.Should().BeTrue("should fall back to app-level when not in workspace");
        _mockFeatureFlagService.Received(1).IsEnabled("console");
    }

    [Test]
    public void IsEnabled_EmptyWorkspaceFeatures_FallsBackToAppLevel()
    {
        // Arrange - Workspace has no features
        var config = new ProjectConfig
        {
            Features = new Dictionary<string, bool>()
        };
        var mockProject = CreateMockProject(config);
        _mockProjectService.CurrentProject.Returns(mockProject);
        _mockFeatureFlagService.IsEnabled("notes-editor").Returns(false);

        // Act
        var result = _workspaceFeatures.IsEnabled("notes-editor");

        // Assert
        result.Should().BeFalse("should use app-level when workspace has no features");
        _mockFeatureFlagService.Received(1).IsEnabled("notes-editor");
    }

    #endregion

    #region No Project Loaded Tests

    [Test]
    public void IsEnabled_NoProjectLoaded_UsesAppLevel()
    {
        // Arrange - No project loaded
        _mockProjectService.CurrentProject.Returns((IProject?)null);
        _mockFeatureFlagService.IsEnabled("notes-editor").Returns(true);

        // Act
        var result = _workspaceFeatures.IsEnabled("notes-editor");

        // Assert
        result.Should().BeTrue("should use app-level when no project loaded");
        _mockFeatureFlagService.Received(1).IsEnabled("notes-editor");
    }

    [Test]
    public void IsEnabled_NoProjectLoaded_ReturnsFalseWhenAppLevelDisabled()
    {
        // Arrange - No project loaded, app-level disabled
        _mockProjectService.CurrentProject.Returns((IProject?)null);
        _mockFeatureFlagService.IsEnabled("notes-editor").Returns(false);

        // Act
        var result = _workspaceFeatures.IsEnabled("notes-editor");

        // Assert
        result.Should().BeFalse("should return false when app-level is disabled and no project");
        _mockFeatureFlagService.Received(1).IsEnabled("notes-editor");
    }

    #endregion

    #region Multiple Features Tests

    [Test]
    public void IsEnabled_MultipleFeatures_HandlesEachIndependently()
    {
        // Arrange - Multiple features with different sources
        var config = new ProjectConfig
        {
            Features = new Dictionary<string, bool>
            {
                ["notes-editor"] = true,
                ["console"] = false
            }
        };
        var mockProject = CreateMockProject(config);
        _mockProjectService.CurrentProject.Returns(mockProject);
        _mockFeatureFlagService.IsEnabled(Arg.Any<string>()).Returns(true);

        // Act & Assert
        _workspaceFeatures.IsEnabled("notes-editor").Should().BeTrue("workspace enables notes-editor");
        _workspaceFeatures.IsEnabled("console").Should().BeFalse("workspace disables console");
        _workspaceFeatures.IsEnabled("code-editor").Should().BeTrue("falls back to app-level for code-editor");

        _mockFeatureFlagService.DidNotReceive().IsEnabled("notes-editor");
        _mockFeatureFlagService.DidNotReceive().IsEnabled("console");
        _mockFeatureFlagService.Received(1).IsEnabled("code-editor");
    }

    #endregion

    #region Helper Methods

    private static IProject CreateMockProject(ProjectConfig config)
    {
        var project = Substitute.For<IProject>();
        project.Config.Returns(config);
        project.ProjectFilePath.Returns("/test/project.celbridge");
        project.ProjectName.Returns("TestProject");
        project.ProjectFolderPath.Returns("/test");
        project.ProjectDataFolderPath.Returns("/test/.celbridge");
        return project;
    }

    #endregion
}
