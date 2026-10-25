using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.Resources.Services;

namespace Celbridge.Tests.FileSystem;

/// <summary>
/// Builds a ResourcePolicy for tests that exercise policy-gated services without a live workspace. The default
/// policy has empty Explorer and search lists.
/// </summary>
internal static class TestResourcePolicy
{
    public static ResourcePolicy CreateDefault()
    {
        return new ResourcePolicy(new NullProjectService());
    }

    /// <summary>
    /// Builds a policy with the patterns as its search list. The policy stands up its own substitutes, so
    /// build it into a local before configuring another substitute to return it.
    /// </summary>
    public static ResourcePolicy CreateWithSearchExclude(string projectFolderPath, string[] searchExcludePatterns)
    {
        var config = new ProjectConfig
        {
            Search = new SearchSection { Exclude = searchExcludePatterns },
        };

        var project = Substitute.For<IProject>();
        project.Config.Returns(config);
        project.ProjectFolderPath.Returns(projectFolderPath);

        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        return new ResourcePolicy(projectService);
    }

    // A hand-written stub rather than an NSubstitute mock so CreateDefault
    // touches no substitute state. That lets callers inline it inside an
    // NSubstitute Returns(...) without polluting the "last call" context.
    // ResourcePolicy only reads CurrentProject, so the rest throws.
    private sealed class NullProjectService : IProjectService
    {
        public IProject? CurrentProject => null;

        public Result ValidateNewProjectConfig(NewProjectConfig config) => throw new NotImplementedException();
        public Task<Result<IReadOnlyList<string>>> GetConflictingFileNamesAsync(NewProjectConfig config) => throw new NotImplementedException();
        public Task<Result<IReadOnlyList<string>>> FindProjectFilesAsync(string folderPath) => throw new NotImplementedException();
        public Task<Result> CreateProjectAsync(NewProjectConfig config, bool replaceExistingFiles = false) => throw new NotImplementedException();
        public Task<Result<IProject>> LoadProjectAsync(string projectFilePath, MigrationResult migrationResult) => throw new NotImplementedException();
        public Task<ProjectConfigReconcileResult?> ReconcileConfigAsync(IReadOnlyList<EditorContribution> discoveredContributions, bool persistNormalizedConfig) => throw new NotImplementedException();
        public void ClearCurrentProject() => throw new NotImplementedException();
        public List<RecentProject> GetRecentProjects(bool excludeCurrentProject) => throw new NotImplementedException();
        public void ClearRecentProjects() => throw new NotImplementedException();
    }
}
