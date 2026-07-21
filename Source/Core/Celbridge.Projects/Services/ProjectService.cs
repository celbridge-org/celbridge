using Celbridge.Logging;
using Celbridge.Packages;
using Celbridge.Settings;
using Celbridge.Utilities;

namespace Celbridge.Projects.Services;

public class ProjectService : IProjectService
{
    private const int RecentProjectsMax = 10;

    private readonly ILogger<ProjectService> _logger;
    private readonly ISettingsService _settingsService;
    private readonly ProjectFactory _projectFactory;
    private readonly IProjectTemplateService _projectTemplateService;
    private readonly ILocalFileSystem _fileSystem;

    public IProject? CurrentProject { get; private set; }

    public ProjectService(
        ILogger<ProjectService> logger,
        ISettingsService settingsService,
        ProjectFactory projectFactory,
        IProjectTemplateService projectTemplateService,
        ILocalFileSystem fileSystem)
    {
        _logger = logger;
        _settingsService = settingsService;
        _projectFactory = projectFactory;
        _projectTemplateService = projectTemplateService;
        _fileSystem = fileSystem;
    }

    public Result ValidateNewProjectConfig(NewProjectConfig config)
    {
        if (config is null)
        {
            return Result.Fail("New project config is null.");
        }

        if (string.IsNullOrWhiteSpace(config.ProjectFilePath))
        {
            return Result.Fail("Project file path is empty.");
        }

        var projectName = Path.GetFileName(config.ProjectFilePath);        
        if (!ResourceKey.IsValidSegment(projectName))
        {
            return Result.Fail($"Project name is not valid: '{projectName}'");
        }

        var extension = Path.GetExtension(projectName);
        if (extension != ProjectConstants.ProjectFileExtension)
        {
            return Result.Fail($"Project file extension is not valid: '{projectName}'");
        }

        return Result.Ok();
    }

    public async Task<Result> CreateProjectAsync(NewProjectConfig config)
    {
        try
        {
            var projectFilePath = config.ProjectFilePath;
            var existingInfo = await _fileSystem.GetInfoAsync(projectFilePath);
            if (existingInfo.IsSuccess && existingInfo.Value.Kind == StorageItemKind.File)
            {
                return Result.Fail($"Failed to create project file because the file already exists: '{projectFilePath}'");
            }

            var createResult = await _projectTemplateService.CreateFromTemplateAsync(config.ProjectFilePath, config.Template);
            if (createResult.IsFailure)
            {
                return Result.Fail($"Failed to create project: '{config.ProjectFilePath}'")
                    .WithErrors(createResult);
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occured when creating project: '{config.ProjectFilePath}'")
                .WithException(ex);
        }
    }

    public async Task<Result<IProject>> LoadProjectAsync(string projectFilePath, MigrationResult migrationResult)
    {
        try
        {
            var loadResult = await _projectFactory.LoadAsync(projectFilePath, migrationResult);
            if (loadResult.IsFailure)
            {
                return Result<IProject>.Fail($"Failed to load project: {projectFilePath}")
                    .WithErrors(loadResult);
            }

            // Project has successfully loaded, so we can now populate the member variables
            CurrentProject = loadResult.Value;

            // Update the recent projects list. Copy before mutating: an unconfigured
            // read returns the descriptor's shared default list instance.
            var storedProjects = _settingsService.Get(SettingCatalog.Project.RecentProjects);
            var recentProjects = new List<string>(storedProjects);
            recentProjects.Remove(projectFilePath);
            recentProjects.Insert(0, projectFilePath);
            while (recentProjects.Count > RecentProjectsMax)
            {
                recentProjects.RemoveAt(recentProjects.Count - 1);
            }
            _settingsService.Set(SettingCatalog.Project.RecentProjects, recentProjects);

            return Result<IProject>.Ok(CurrentProject);
        }
        catch (Exception ex)
        {
            return Result<IProject>.Fail($"An exception occurred when loading the project database.")
                .WithException(ex);
        }
    }

    public async Task<ProjectConfigReconcileResult?> ReconcileConfigAsync(
        IReadOnlyList<EditorContribution> discoveredContributions,
        bool persistNormalizedConfig)
    {
        var currentProject = CurrentProject;
        var config = currentProject?.Config;
        if (config is null)
        {
            return null;
        }

        var result = ProjectConfigReconciler.Reconcile(config, discoveredContributions);

        if (persistNormalizedConfig)
        {
            await PersistNormalizedConfigAsync(currentProject!, result.Config);
        }

        return result;
    }

    // Writes the normalized config back to the project file, skipping the rewrite when it would clobber
    // content the user still needs. A write failure is logged rather than propagated, so a failed persist
    // never discards the reconcile result the caller relies on.
    private async Task PersistNormalizedConfigAsync(IProject project, ProjectConfig normalizedConfig)
    {
        // Never rewrite a file that did not parse cleanly: a faulted load carries an empty config, and
        // writing it would overwrite the user's broken .celbridge with a fresh default, destroying the
        // content they need to hand-fix and reload.
        if (!project.ConfigIsHealthy)
        {
            return;
        }

        // Skip the normalize rewrite when the file parsed but carried recoverable entry errors (an
        // unknown key, a malformed entry, a section authored by a newer Celbridge). The canonical
        // rewrite would silently drop that content; leaving the file untouched preserves it for the
        // user to repair, and a clean reload will normalize it then.
        if (project.Config.EntryErrors.Count > 0)
        {
            return;
        }

        var projectFilePath = project.ProjectFilePath;
        if (string.IsNullOrEmpty(projectFilePath))
        {
            return;
        }

        var serialized = ProjectConfigSerializer.Serialize(normalizedConfig);

        var readResult = await _fileSystem.ReadAllTextAsync(projectFilePath);
        if (readResult.IsSuccess)
        {
            var existing = LineEndingHelper.ConvertLineEndings(readResult.Value, "\n");
            if (string.Equals(existing, serialized, StringComparison.Ordinal))
            {
                return;
            }
        }

        var writeResult = await _fileSystem.WriteAllTextAsync(projectFilePath, serialized);
        if (writeResult.IsFailure)
        {
            _logger.LogWarning(writeResult, "Failed to write the normalized project config.");
        }
    }

    public void ClearCurrentProject()
    {
        CurrentProject = null;
    }

    public List<RecentProject> GetRecentProjects()
    {
        var currentProjectPath = CurrentProject?.ProjectFilePath;
        var recentProjects = new List<RecentProject>();

        foreach (var projectFilePath in _settingsService.Get(SettingCatalog.Project.RecentProjects))
        {
            // Bridge the async gateway to the sync caller. GetInfoAsync is
            // a single stat with no continuation work.
            var infoResult = SyncRunner.Run(() => _fileSystem.GetInfoAsync(projectFilePath));
            if (infoResult.IsFailure || infoResult.Value.Kind != StorageItemKind.File)
            {
                continue;
            }

            // Skip the currently opened project
            if (currentProjectPath != null &&
                string.Equals(projectFilePath, currentProjectPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            recentProjects.Add(new RecentProject(projectFilePath));
        }

        return recentProjects;
    }

    public void ClearRecentProjects()
    {
        var currentProjectPath = CurrentProject?.ProjectFilePath;
        
        if (currentProjectPath != null)
        {
            // Keep only the currently opened project in the list
            var openProjectOnly = new List<string>
            {
                currentProjectPath
            };
            _settingsService.Set(SettingCatalog.Project.RecentProjects, openProjectOnly);
        }
        else
        {
            // No project is open, clear everything
            var emptyProjectList = new List<string>();
            _settingsService.Set(SettingCatalog.Project.RecentProjects, emptyProjectList);
        }
    }
}
