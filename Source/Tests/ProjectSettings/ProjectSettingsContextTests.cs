using Celbridge.Commands;
using Celbridge.Packages;
using Celbridge.ProjectSettings.ViewModels;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Settings;
using Celbridge.Workspace;

namespace Celbridge.Tests.ProjectSettings;

/// <summary>
/// Covers the config the Project Settings sections present: the project file's current content, not the
/// config the loaded project was built from.
/// </summary>
[TestFixture]
public class ProjectSettingsContextTests
{
    [Test]
    public void GetConfig_ReturnsTheProjectFileRatherThanTheLoadedConfig()
    {
        var context = CreateContext(normalizedConfig: null);
        context.Draft = new ProjectConfigDraft(CreateFileConfig());

        var config = context.GetConfig();

        AssertIsTheFileConfig(config);
    }

    [Test]
    public void GetConfig_ReturnsTheProjectFileWhenTheLoadedProjectWasReconciled()
    {
        var context = CreateContext(normalizedConfig: CreateLoadedConfig());
        context.Draft = new ProjectConfigDraft(CreateFileConfig());

        var config = context.GetConfig();

        AssertIsTheFileConfig(config);
    }

    [Test]
    public void GetConfig_BeforeADraftIsLoaded_ReturnsTheLoadedConfig()
    {
        var context = CreateContext(normalizedConfig: null);

        var config = context.GetConfig();

        config.Should().NotBeNull();
        config!.Celbridge.Description.Should().Be("From the load");
    }

    private static void AssertIsTheFileConfig(ProjectConfig? config)
    {
        config.Should().NotBeNull();
        config!.Celbridge.Description.Should().Be("From the file");
        config.Resources.Hide.Should().Equal("*.tmp");
        config.Features[FeatureFlagConstants.NoteEditor].Should().BeTrue();
    }

    private static ProjectConfig CreateLoadedConfig()
    {
        return new ProjectConfig
        {
            Celbridge = new CelbridgeSection
            {
                Description = "From the load"
            }
        };
    }

    private static ProjectConfig CreateFileConfig()
    {
        return new ProjectConfig
        {
            Celbridge = new CelbridgeSection
            {
                Description = "From the file"
            },
            Resources = new ResourcesSection
            {
                Hide = ["*.tmp"],
                SearchExclude = []
            },
            Features = new Dictionary<string, bool>
            {
                [FeatureFlagConstants.NoteEditor] = true
            }
        };
    }

    private static ProjectSettingsContext CreateContext(ProjectConfig? normalizedConfig)
    {
        var project = Substitute.For<IProject>();
        project.Config.Returns(CreateLoadedConfig());

        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        var packageService = Substitute.For<IPackageService>();
        packageService.GetNormalizedConfig().Returns(normalizedConfig);
        packageService.GetAllPackages().Returns([]);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.PackageService.Returns(packageService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        return new ProjectSettingsContext(
            workspaceWrapper,
            projectService,
            Substitute.For<ICommandService>(),
            () => { });
    }
}
