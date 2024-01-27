using CommunityToolkit.Mvvm.Messaging;
using Celbridge.Legacy.Tasks;

namespace Celbridge.Legacy.Services;

public interface IProject
{
    public string Name { get; }
    public string ProjectPath { get; }
    public string ProjectFolder { get; }
    public string LibraryFolder { get; }
    public ResourceRegistry ResourceRegistry { get; }
}

public interface IProjectService
{
    Project? ActiveProject { get; }
    Result GetProjectPath(string parentFolder, string projectName);
    Task<Result<string>> CreateProject(string parentFolder, string projectName);
    Result SaveProject();
    Task<Result> LoadProject(string projectFile);
    Task<Result> CloseProject();
    Task<Result> OpenPreviousProject();
}

public record ProjectCreatedMessage(Project Project);

public record ActiveProjectChangedMessage(Project? Project);

public record PreviouslySelectedEntityMessage(Guid EntityId);

public class ProjectService : IProjectService, ISaveData
{
    private readonly IMessenger _messengerService;
    private readonly ISettingsService _settingsService;
    private readonly ISaveDataService _saveDataService;
    private readonly IResourceService _resourceService;
    private readonly IDocumentService _documentService;
    private readonly IDialogService _dialogService;
    private readonly IInspectorService _inspectorService;

    public Project? ActiveProject { get; private set; }

    public ProjectService(IMessenger messengerService, 
        ISettingsService settingsService,
        ISaveDataService saveDataService,
        IResourceService resourceService,
        IDocumentService documentService,
        IDialogService dialogService,
        IInspectorService inspectorService)
    {
        _messengerService = messengerService;
        _settingsService = settingsService;
        _saveDataService = saveDataService;
        _resourceService = resourceService;
        _documentService = documentService;
        _dialogService = dialogService;
        _inspectorService = inspectorService;
    }

    public Result GetProjectPath(string parentFolder, string projectName)
    {
        try
        {
            if (string.IsNullOrEmpty(parentFolder) || string.IsNullOrEmpty(projectName))
            {
                return new ErrorResult("Invalid project path");
            }

            if (projectName.Contains('\\') || projectName.Contains('/')) 
            {
                return new ErrorResult($"Project name may not contain folder separators: {projectName}");
            }

            var projectPath = Path.Combine(parentFolder, projectName, projectName);
            projectPath = Path.ChangeExtension(projectPath, Constants.ProjectFileExtension);

            if (FileUtils.IsAbsolutePathValid(projectPath))
            {
                return new SuccessResult<string>(projectPath);
            }
            else
            {
                return new ErrorResult($"Invalid project path: {projectPath}");
            }
        }
        catch (Exception)
        {
            return new ErrorResult("Invalid project path");
        }
    }

    public async Task<Result<string>> CreateProject(string parentFolder, string projectName)
    {
        if (ActiveProject != null)
        {
            var closeResult = await CloseProject();
            if (closeResult.Failure)
            {
                var closeError = closeResult as ErrorResult;
                return new ErrorResult<string>($"Failed to close active project: {closeError!.Message}");
            }
        }

        // Get a valid project path
        var projectPathResult = GetProjectPath(parentFolder, projectName);
        if (projectPathResult is ErrorResult error)
        {
            return new ErrorResult<string>(error.Message);
        }


        var projectPath = (projectPathResult as SuccessResult<string>)!.Data!;
        Guard.IsNotNull(projectPath);

        try
        {
            // Get the folder and filename from the validated path.
            string folder = Path.GetDirectoryName(projectPath) ?? string.Empty;
            string filename = Path.GetFileName(projectPath);

            if (Directory.Exists(folder))
            {
                return new ErrorResult<string>($"Project folder already exists: {folder}");
            }

            Directory.CreateDirectory(folder);

            var project = new Project()
            {
                Id = Guid.NewGuid(),
                Name = projectName,
                ProjectPath = projectPath,
            };

            string json = JsonConvert.SerializeObject(project, Formatting.Indented);
            await File.WriteAllTextAsync(projectPath, json);

            // Remember last opened project
            Guard.IsNotNull(_settingsService.EditorSettings);
            _settingsService.EditorSettings.PreviousActiveProjectPath = projectPath;

            var message = new ProjectCreatedMessage(project);
            _messengerService.Send(message); 

            return new SuccessResult<string>(projectPath);
        }
        catch (Exception ex)
        {
            return new ErrorResult<string>(ex.Message);
        }
    }

    public Result SaveProject()
    {
        if (ActiveProject == null)
        {
            return new ErrorResult("Failed to save active project because none is loaded.");
        }

        _saveDataService.RequestSave(this);

        return new SuccessResult();
    }

    private Result<string> SerializeActiveProject()
    {
        try
        {
            JsonSerializerSettings jsonSettings = new()
            {
                TypeNameHandling = TypeNameHandling.Auto,
                Formatting = Formatting.Indented,
            };

            string json = JsonConvert.SerializeObject(ActiveProject, jsonSettings);
            return new SuccessResult<string>(json);
        }
        catch (Exception ex)
        {
            return new ErrorResult<string>(ex.Message);
        }
    }

    public async Task<Result> SaveAsync()
    {
        var jsonResult = SerializeActiveProject();
        if (jsonResult.Failure)
        {
            return new ErrorResult("Failed to serialize active project.");
        }

        Guard.IsNotNull(ActiveProject);

        await File.WriteAllTextAsync(ActiveProject.ProjectPath, jsonResult.Data);
        // Log.Information("Saved Project");

        return new SuccessResult();
    }

    public async Task<Result> LoadProject(string projectPath)
    {
        Guid forceSelectEntityGuid = Guid.Empty;

        if (ActiveProject != null)
        {
            if (ActiveProject.ProjectPath == projectPath)
            {
                // We're closing and reopening the same project (i.e. a refresh)
                // Remember the currently selected entity so we can reselect it below
                // when the project reopens.
                if (_inspectorService.SelectedEntity != null)
                {
                    forceSelectEntityGuid = _inspectorService.SelectedEntity.Id;
                }
            }

            var result = await CloseProject();
            if (result.Failure)
            {
                var error = result as ErrorResult;
                return new ErrorResult($"Failed to close active project: {error!.Message}");
            }
        }

        try
        {
            var showProgressResult = _dialogService.ShowProgressDialog("Load Project", null);
            if (showProgressResult is ErrorResult showProgressError)
            {
                return new ErrorResult($"Failed to show progress dialog: {showProgressError.Message}");
            }

            var services = LegacyServiceProvider.Services!;
            var loadProjectTask = services.GetRequiredService<LoadProjectTask>();

            var result = await loadProjectTask.Load(projectPath);
            if (result is ErrorResult<Project> loadError)
            {
                Log.Error(loadError.Message);
                _dialogService.HideProgressDialog();
                return new ErrorResult(loadError.Message);
            }

            var project = result.Data!;
            Guard.IsNotNull(project);

            ActiveProject = project;
            var activeProjectChanged = new ActiveProjectChangedMessage(project);
            _messengerService.Send(activeProjectChanged);

            // Todo: Await this call, maybe using an OpenDocumentsTask class?
            // It probably only works right now because of the delay below
            OpenPreviousDocuments();

            Guard.IsNotNull(_settingsService.ProjectSettings);
            var previousSelectedEntity = _settingsService.ProjectSettings.SelectedEntity;
            if (previousSelectedEntity == Guid.Empty &&
                forceSelectEntityGuid != Guid.Empty)
            {
                // Use the selected entity we noted above.
                previousSelectedEntity = forceSelectEntityGuid;
            }

            if (previousSelectedEntity != Guid.Empty)
            {
                // Wait a bit to give the UI time to populate.
                // This is super hacky, but it fixes an issue where we try to set the selected item on the resource tree view before
                // it has finished populating. There doesn't seem to be a callback to let you know when it's done.
                await Task.Delay(250);

                // Broadcast the previously selected entity so services and open documents can check if they own that entity and select it.
                var entityMessage = new PreviouslySelectedEntityMessage(previousSelectedEntity);
                _messengerService.Send(entityMessage);
            }

            var hideProgressResult= _dialogService.HideProgressDialog();
            if (hideProgressResult is ErrorResult hideProgressError)
            {
                return new ErrorResult(hideProgressError.Message);
            }

            return result;
        }
        catch (Exception ex)
        {
            return new ErrorResult(ex.Message);
        }
    }

    private void OpenPreviousDocuments()
    {
        Guard.IsNotNull(ActiveProject);
        Guard.IsNotNull(_settingsService.ProjectSettings);

        // Opening a document can modify the ProjectSettings.OpenDocuments list, triggering a collection modified exception.
        // We make a copy of the list to avoid this issue.
        var previouslyOpenedDocuments = _settingsService.ProjectSettings.OpenDocuments.ToList();
        var failedToOpenDocuments = new List<Guid>();
        foreach (var entityId in previouslyOpenedDocuments)
        {
            var findResult = _resourceService.FindResourceEntity(ActiveProject, entityId);
            if (findResult.Failure)
            {
                failedToOpenDocuments.Add(entityId);
                continue;
            }

            var documentEntity = findResult.Data as IDocumentEntity;
            if (documentEntity != null)
            {
                var openResult = _documentService.OpenDocument(documentEntity);
                if (openResult.Failure)
                {
                    failedToOpenDocuments.Add(entityId);
                }
            }
        }

        foreach (var entityId in failedToOpenDocuments)
        {
            // This is an observable collection so better to remove items than assign a new list in case
            // something is observing it.
            previouslyOpenedDocuments.Remove(entityId);
        }
    }

    public async Task<Result> CloseProject()
    {
        if (ActiveProject == null)
        {
            return new ErrorResult("Failed to close project. No project is active.");
        }

        // Wait for any pending save action to complete
        while (_saveDataService.IsSaving)
        {
            await Task.Delay(50);
        }

        var result = _documentService.CloseAllDocuments(true);
        if (result.Failure)
        {
            var error = result as ErrorResult;
            return new ErrorResult($"Failed to close project. {error!.Message}");
        }

        ActiveProject = null;
        Guard.IsNotNull(_settingsService.EditorSettings);

        _settingsService.EditorSettings.PreviousActiveProjectPath = string.Empty;

        var activeProjectChanged = new ActiveProjectChangedMessage(null);
        _messengerService.Send(activeProjectChanged);

        return new SuccessResult();
    }

    public async Task<Result> OpenPreviousProject()
    {
        Guard.IsNotNull(_settingsService.EditorSettings);

        var previousProjectPath = _settingsService.EditorSettings.PreviousActiveProjectPath;
        if (string.IsNullOrEmpty(previousProjectPath))
        {
            return new ErrorResult("No previously opened project");
        }

        var loadResult = await LoadProject(previousProjectPath);
        if (loadResult is ErrorResult loadError)
        {
            return new ErrorResult($"Failed to open previously opened project: {previousProjectPath}. {loadError.Message}");
        }

        return new SuccessResult();
    }
}
