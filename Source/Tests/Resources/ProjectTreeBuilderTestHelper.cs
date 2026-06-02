using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Builds a ProjectTreeBuilder wired to a real ResourcePolicy with the
/// default-permissive [resources] configuration. Used by tests that exercise
/// the project tree walker without a live workspace.
/// </summary>
internal static class ProjectTreeBuilderTestHelper
{
    public static ProjectTreeBuilder Build(IFileIconService? fileIconService = null)
    {
        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourcePolicy.Returns(TestResourcePolicy.CreateDefault());

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        return new ProjectTreeBuilder(fileIconService ?? new FileIconService(), workspaceWrapper);
    }
}
