using Celbridge.Resources.Services;
using Celbridge.Resources.Services.Roots;

namespace Celbridge.Tests.FileSystem;

/// <summary>
/// Builds a RootHandlerRegistry for tests that resolve resource keys without a live workspace. The registry contains
/// the project root's real handler over the folder, so keys go through the same link and stored name checks as in
/// the application.
/// </summary>
internal static class TestRootHandlers
{
    public static RootHandlerRegistry Create(string projectFolderPath)
    {
        var rootHandlers = new RootHandlerRegistry();
        rootHandlers.RegisterRootHandler(new ProjectRootHandler(projectFolderPath));
        return rootHandlers;
    }
}
