using Celbridge.Navigation;
using Celbridge.Settings;
using Celbridge.Workspace;
using Newtonsoft.Json.Linq;

using Path = System.IO.Path;

namespace Celbridge.Projects.Services;

public class ProjectService : IProjectService
{
    private const int RecentProjectsMax = 5;

    private readonly IEditorSettings _editorSettings;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly INavigationService _navigationService;

    private const string ProjectDataFileKey = "projectDataFile";

    private const string EmptyPageName = "EmptyPage";

    public IProject? LoadedProject { get; private set; }

    public ProjectService(
        IEditorSettings editorSettings,
        IWorkspaceWrapper workspaceWrapper,
        INavigationService navigationService)
    {
        _editorSettings = editorSettings;
        _workspaceWrapper = workspaceWrapper;
        _navigationService = navigationService;
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
        if (extension != FileNames.ProjectFileExtension)
        {
            return Result.Fail($"Project file extension is not valid: '{projectName}'");
        }

        return Result.Ok();
    }

    public async Task<Result> CreateProjectAsync(NewProjectConfig config)
    {
        try
        {
            // Todo: Create the data files in a temp directory first and move them into place when all operations succeed

            // Ensure the project folder exists

            var projectFilePath = config.ProjectFilePath;
            if (File.Exists(projectFilePath))
            {
                return Result.Fail($"Failed to create project file because the file already exists: '{projectFilePath}'");
            }

            var projectFolderPath = Path.GetDirectoryName(projectFilePath);
            if (string.IsNullOrEmpty(projectFolderPath))
            {
                return Result.Fail($"Failed to get folder for project file path: '{projectFilePath}'");
            }

            if (!Directory.Exists(projectFolderPath))
            {
                Directory.CreateDirectory(projectFolderPath);
            }

            // Write the project JSON file in the project folder
            var projectJson = $$"""
                {
                    "{{ProjectDataFileKey}}": "{{FileNames.ProjectDataFolder}}/{{FileNames.ProjectDataFile}}",
                }
                """;

            await File.WriteAllTextAsync(config.ProjectFilePath, projectJson);

            //
            // Create a database file inside a folder named after the project
            //

            var databasePath = Path.Combine(projectFolderPath, FileNames.ProjectDataFolder, FileNames.ProjectDataFile);
            string dataFolderPath = Path.GetDirectoryName(databasePath)!;
            Directory.CreateDirectory(dataFolderPath);

            var createResult = await Project.CreateProjectAsync(config.ProjectFilePath, databasePath);
            if (createResult.IsFailure)
            {
                return Result.Fail($"Failed to create project database: '{databasePath}'");
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, $"Failed to create project: '{config.ProjectFilePath}'");
        }
    }

    public Result LoadProject(string projectPath)
    {
        try
        {
            var projectJsonData = File.ReadAllText(projectPath);
            var jsonObject = JObject.Parse(projectJsonData);
            Guard.IsNotNull(jsonObject);

            var projectFolderPath = Path.GetDirectoryName(projectPath)!; 

            string projectDataPathRelative = jsonObject["projectDataFile"]!.ToString();
            string projectDataPath = Path.GetFullPath(Path.Combine(projectFolderPath, projectDataPathRelative));

            var loadResult = Project.LoadProject(projectPath, projectDataPath);
            if (loadResult.IsFailure)
            {
                return Result.Fail($"Failed to load project database: {projectDataPath}");
            }

            // Both data files have successfully loaded, so we can now populate the member variables
            LoadedProject = loadResult.Value;

            // Update the recent projects list in editor settings
            var recentProjects = _editorSettings.RecentProjects;
            recentProjects.Remove(projectPath);
            recentProjects.Insert(0, projectPath);
            while (recentProjects.Count > RecentProjectsMax)
            {
                recentProjects.RemoveAt(recentProjects.Count - 1);
            }
            _editorSettings.RecentProjects = recentProjects;

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to load project database. {ex.Message}");
        }
    }

    public async Task<Result> UnloadProjectAsync()
    {
        if (LoadedProject is null)
        {
            // Unloading a project that is not loaded is a no-op
            return Result.Ok();
        }

        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Failed to unload project data because no project is loaded");
        }

        // Todo: Notify the workspace that it is about to close.
        // The workspace may want to perform some operations (e.g. save changes) before we close it.

        // Force the Workspace page to unload by navigating to an empty page.
        _navigationService.NavigateToPage(EmptyPageName);

        // Wait until the workspace is fully unloaded
        while (_workspaceWrapper.IsWorkspacePageLoaded)
        {
            await Task.Delay(50);
        }

        var disposableProject = LoadedProject as IDisposable;
        Guard.IsNotNull(disposableProject);
        disposableProject.Dispose();
        LoadedProject = null;

        return Result.Ok();
    }
}
