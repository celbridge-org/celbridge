using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Builds a ProjectTreeBuilder wired to a real LocalResourceFileSystem over the
/// supplied project folder. By default the [celbridge.resources] settings are empty.
/// Pass searchExcludePatterns to bound the walk. The builder enumerates through the
/// gateway, so the helper stands up the resource file system and a registry that
/// resolves keys to paths under the project folder.
/// </summary>
internal static class ProjectTreeBuilderTestHelper
{
    public static ProjectTreeBuilder Build(
        string projectFolderPath,
        IIconService? iconService = null,
        string[]? searchExcludePatterns = null)
    {
        var resourceRegistry = Substitute.For<IResourceRegistry>();
        resourceRegistry.ProjectFolderPath.Returns(projectFolderPath);
        resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>(), Arg.Any<bool>()).Returns(callInfo =>
        {
            var resourceKey = callInfo.Arg<ResourceKey>();
            var relativePath = resourceKey.Path.Replace('/', Path.DirectorySeparatorChar);
            var absolutePath = string.IsNullOrEmpty(relativePath)
                ? projectFolderPath
                : Path.Combine(projectFolderPath, relativePath);

            return Result<string>.Ok(absolutePath);
        });

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        // Build the policy into a local before configuring the substitute: when
        // it stands up its own substitutes, doing so inline inside Returns(...)
        // would corrupt NSubstitute's last-call context.
        var policy = BuildPolicy(projectFolderPath, searchExcludePatterns);
        resourceService.Policy.Returns(policy);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            Substitute.For<IMessengerService>(),
            workspaceWrapper,
            TestFileSystem.CreateLocal());
        resourceService.FileSystem.Returns(resourceFileSystem);

        return new ProjectTreeBuilder(iconService ?? new IconService(), workspaceWrapper);
    }

    private static IResourcePolicy BuildPolicy(string projectFolderPath, string[]? searchExcludePatterns)
    {
        if (searchExcludePatterns is null
            || searchExcludePatterns.Length == 0)
        {
            return TestResourcePolicy.CreateDefault();
        }

        var resources = new ResourcesSection
        {
            SearchExclude = searchExcludePatterns
        };

        var project = Substitute.For<IProject>();
        project.Config.Returns(new ProjectConfig { Resources = resources });
        project.ProjectFolderPath.Returns(projectFolderPath);

        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        return new ResourcePolicy(projectService);
    }
}
