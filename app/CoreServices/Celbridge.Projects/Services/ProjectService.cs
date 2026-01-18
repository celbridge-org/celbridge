using Celbridge.Settings;

using Path = System.IO.Path;

namespace Celbridge.Projects.Services;

public class ProjectService : IProjectService
{
    private const int RecentProjectsMax = 10;

    private readonly IEditorSettings _editorSettings;

    public IProject? CurrentProject { get; private set; }

    public ProjectService(
        IEditorSettings editorSettings)
    {
        _editorSettings = editorSettings;
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
            if (File.Exists(projectFilePath))
            {
                return Result.Fail($"Failed to create project file because the file already exists: '{projectFilePath}'");
            }

            var createResult = await Project.CreateProjectAsync(config.ProjectFilePath, config.ConfigType);
            if (createResult.IsFailure)
            {
                return Result.Fail($"Failed to create project: '{config.ProjectFilePath}'");
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
            var loadResult = await Project.LoadProjectAsync(projectFilePath, migrationResult);
            if (loadResult.IsFailure)
            {
                return Result<IProject>.Fail($"Failed to load project: {projectFilePath}")
                    .WithErrors(loadResult);
            }

            // Project has successfully loaded, so we can now populate the member variables
            CurrentProject = loadResult.Value;

            // Update the recent projects list in editor settings
            var recentProjects = _editorSettings.RecentProjects;
            recentProjects.Remove(projectFilePath);
            recentProjects.Insert(0, projectFilePath);
            while (recentProjects.Count > RecentProjectsMax)
            {
                recentProjects.RemoveAt(recentProjects.Count - 1);
            }
            _editorSettings.RecentProjects = recentProjects;

            return Result<IProject>.Ok(CurrentProject);
        }
        catch (Exception ex)
        {
            return Result<IProject>.Fail($"An exception occurred when loading the project database.")
                .WithException(ex);
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

        foreach (var projectFilePath in _editorSettings.RecentProjects)
        {
            if (!File.Exists(projectFilePath))
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
            _editorSettings.RecentProjects = new List<string> { currentProjectPath };
        }
        else
        {
            // No project is open, clear everything
            _editorSettings.RecentProjects = new List<string>();
        }
    }
}
