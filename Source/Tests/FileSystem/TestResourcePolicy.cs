using Celbridge.Projects;
using Celbridge.Resources.Services;

namespace Celbridge.Tests.FileSystem;

/// <summary>
/// Builds a ResourcePolicy with the default-permissive [resources] configuration
/// for tests that exercise policy-gated services without a live workspace.
/// </summary>
internal static class TestResourcePolicy
{
    public static ResourcePolicy CreateDefault()
    {
        return new ResourcePolicy(new NullProjectService());
    }

    // A hand-written stub rather than an NSubstitute mock so CreateDefault
    // touches no substitute state. That lets callers inline it inside an
    // NSubstitute Returns(...) without polluting the "last call" context.
    // ResourcePolicy only reads CurrentProject, so the rest throws.
    private sealed class NullProjectService : IProjectService
    {
        public IProject? CurrentProject => null;

        public Result ValidateNewProjectConfig(NewProjectConfig config) => throw new NotImplementedException();
        public Task<Result> CreateProjectAsync(NewProjectConfig config) => throw new NotImplementedException();
        public Task<Result<IProject>> LoadProjectAsync(string projectFilePath, MigrationResult migrationResult) => throw new NotImplementedException();
        public void ClearCurrentProject() => throw new NotImplementedException();
        public List<RecentProject> GetRecentProjects() => throw new NotImplementedException();
        public void ClearRecentProjects() => throw new NotImplementedException();
    }
}
