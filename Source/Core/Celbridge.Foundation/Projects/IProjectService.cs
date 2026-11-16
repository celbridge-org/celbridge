using Celbridge.Packages;

namespace Celbridge.Projects;

/// <summary>
/// Provides services for managing projects.
/// </summary>
public interface IProjectService
{
    /// <summary>
    /// Returns the current loaded project.
    /// </summary>
    IProject? CurrentProject { get; }

    /// <summary>
    /// Check if a new project config is valid.
    /// </summary>
    Result ValidateNewProjectConfig(NewProjectConfig config);

    /// <summary>
    /// Returns the names of the files the configured template writes that already exist in the
    /// destination folder, sorted by name.
    /// </summary>
    Task<Result<IReadOnlyList<string>>> GetConflictingFileNamesAsync(NewProjectConfig config);

    /// <summary>
    /// Returns the full paths of the project files directly in the folder, ordered by path. A missing folder
    /// gives an empty list.
    /// </summary>
    Task<Result<IReadOnlyList<string>>> FindProjectFilesAsync(string folderPath);

    /// <summary>
    /// Create a new project file and database using the specified config information. Fails without
    /// writing anything when the destination folder already contains a project file. Also fails when the
    /// folder contains one of the files the template writes, unless replaceExistingFiles asks for those
    /// files to be replaced.
    /// </summary>
    Task<Result> CreateProjectAsync(NewProjectConfig config, bool replaceExistingFiles = false);

    /// <summary>
    /// Load the project file at the specified path.
    /// The migrationResult should be obtained from ProjectLoader before calling this method. isTrusted says
    /// whether the project opens trusted.
    /// </summary>
    Task<Result<IProject>> LoadProjectAsync(string projectFilePath, MigrationResult migrationResult, bool isTrusted);

    /// <summary>
    /// Returns true when the user trusts the project at the given path on this machine. The project data folder
    /// records the trust.
    /// </summary>
    Task<bool> IsTrustedAsync(string projectFilePath);

    /// <summary>
    /// Records that the user trusts the project at the given path on this machine. The trust stays.
    /// </summary>
    Task<Result> TrustProjectAsync(string projectFilePath);

    /// <summary>
    /// From this call on, the instance treats every project it opens as trusted. Nothing records the trust. Only a
    /// Debug build supports the call. In a release build, the call throws NotSupportedException.
    /// </summary>
    void AssumeTrusted();

    /// <summary>
    /// Reconciles the current project's config against the discovered editor contributions and returns
    /// the resolved active set plus the normalized config. Null when no project is loaded. When
    /// persistNormalizedConfig is set, the normalized config is also written back to the project file,
    /// unless the load was unhealthy or carried recoverable entry errors.
    /// </summary>
    Task<ProjectConfigReconcileResult?> ReconcileConfigAsync(
        IReadOnlyList<EditorContribution> discoveredContributions,
        bool persistNormalizedConfig);

    /// <summary>
    /// Clears the CurrentProject reference without disposing it.
    /// The caller is responsible for disposing the project.
    /// </summary>
    void ClearCurrentProject();

    /// <summary>
    /// Returns the recent projects that still exist on disk, most recently opened first. Excluding the
    /// current project suits menus that list the open project separately.
    /// </summary>
    List<RecentProject> GetRecentProjects(bool excludeCurrentProject);

    /// <summary>
    /// Clears the list of recently opened projects.
    /// </summary>
    void ClearRecentProjects();
}
