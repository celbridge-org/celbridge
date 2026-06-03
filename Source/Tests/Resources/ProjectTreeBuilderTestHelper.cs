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
/// supplied project folder. By default the [resources] policy is permissive; pass
/// useProjectIgnoreFile to build a real policy that reads the project's ignore-file
/// from disk (write the file before calling Build, since the policy compiles once).
/// The builder enumerates through the gateway, so the helper stands up the resource
/// file system and a registry that resolves keys to paths under the project folder.
/// </summary>
internal static class ProjectTreeBuilderTestHelper
{
    public static ProjectTreeBuilder Build(
        string projectFolderPath,
        IFileIconService? fileIconService = null,
        bool useProjectIgnoreFile = false)
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
        var policy = BuildPolicy(projectFolderPath, useProjectIgnoreFile);
        resourceService.Policy.Returns(policy);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            Substitute.For<IMessengerService>(),
            workspaceWrapper,
            TestFileSystem.CreateLocal());
        resourceService.FileSystem.Returns(resourceFileSystem);

        return new ProjectTreeBuilder(fileIconService ?? new FileIconService(), workspaceWrapper);
    }

    private static IResourcePolicy BuildPolicy(string projectFolderPath, bool useProjectIgnoreFile)
    {
        if (!useProjectIgnoreFile)
        {
            return TestResourcePolicy.CreateDefault();
        }

        var project = Substitute.For<IProject>();
        project.Config.Returns(new ProjectConfig());
        project.ProjectFolderPath.Returns(projectFolderPath);

        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        return new ResourcePolicy(projectService, TestFileSystem.CreateLocal());
    }
}
