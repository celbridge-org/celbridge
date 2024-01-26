namespace CelLegacy.Tasks;

public class LoadProjectTask
{
    private readonly ISettingsService _settingsService;
    private readonly IResourceService _resourceService;

    public LoadProjectTask(ISettingsService settingsService,
                           IResourceService resourceService)
    {
        _settingsService = settingsService;
        _resourceService = resourceService;
    }

    public async Task<Result<Project>> Load(string projectPath)
    {
        if (string.IsNullOrEmpty(projectPath) ||
            !File.Exists(projectPath))
        {
            return new ErrorResult<Project>($"Project file not found: {projectPath}");
        }

        // Todo: Use a static function on Project class to do this name lookup consistently
        string? projectFolder = Path.GetDirectoryName(projectPath);
        Guard.IsNotNull(projectFolder);

        string libraryFolder = Path.Combine(projectFolder, "Library");

        // Load and deserialize the project file data
        string json = await File.ReadAllTextAsync(projectPath);
        var deserializeResult = DeserializeProject(json);
        if (deserializeResult is ErrorResult<Project> deserializeError)
        {
            return new ErrorResult<Project>($"Failed to load project file: {projectPath}. {deserializeError.Message}");
        }
        var project = deserializeResult.Data!;

        // Set the path so we know where to save the file later.
        // The path is not serialized with the project data.
        project.ProjectPath = projectPath;

        // Load any previously persisted project settings
        _settingsService.LoadProjectSettings(project.Id);

        // Remember this as the last opened project
        Guard.IsNotNull(_settingsService.EditorSettings);
        _settingsService.EditorSettings.PreviousActiveProjectPath = projectPath;

        // Populate the resource registry
        var updateRegistryResult = await _resourceService.UpdateProjectResources(project);
        if (updateRegistryResult is ErrorResult<RegistryUpdateSummary> updateRegistryError)
        {
            return new ErrorResult<Project>($"Failed to update resource registry: {projectPath}. {updateRegistryError.Message}");
        }
        var summary = updateRegistryResult.Data!;
        if (summary.WasRegistryModified)
        {
            Log.Information($"Updated resources: Added {summary.Added.Count}, Changed {summary.Changed.Count}, Deleted {summary.Deleted.Count}");
        }

        return new SuccessResult<Project>(project);
    }

    private static Result<Project> DeserializeProject(string json)
    {
        try
        {
            JsonSerializerSettings settings = new()
            {
                TypeNameHandling = TypeNameHandling.Auto,
            };

            var project = JsonConvert.DeserializeObject<Project>(json, settings);
            Guard.IsNotNull(project);

            return new SuccessResult<Project>(project);
        }
        catch (Exception ex)
        {
            return new ErrorResult<Project>(ex.Message);
        }
    }
}
