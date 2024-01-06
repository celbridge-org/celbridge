using Celbridge.Services;
using System.ComponentModel;
using Celbridge.Utils;
using CommunityToolkit.Mvvm.Messaging;

namespace Celbridge.ViewModels
{
    public partial class ProjectViewModel : ObservableObject
    {
        private readonly ISettingsService _settingsService;
        private readonly IProjectService _projectService;
        private readonly IResourceService _resourceService;
        private readonly IDialogService _dialogService;
        private readonly IInspectorService _inspectorService;
        private readonly IMessenger _messengerService;

        private Project? _activeProject;
        public Project? ActiveProject
        {
            get => _activeProject;
            set
            {
                SetProperty(ref _activeProject, value);
            }
        }

        [ObservableProperty]
        private bool _hasActiveProject;

        private bool _isProjectSelected;
        public bool IsProjectSelected
        {
            get => _isProjectSelected;
            set
            {
                SetProperty(ref _isProjectSelected, value);
            }
        }

        private object? _selectedResource;
        public object? SelectedResource
        {
            get => _selectedResource;
            set
            {
                SetProperty(ref _selectedResource, value);
            }
        }

        [ObservableProperty]
        private bool _isRefreshProjectEnabled;

        private FolderWatcher? _folderWatcher;

        public ProjectViewModel(ISettingsService settingsService, 
                                IProjectService projectService,
                                IResourceService resourceService,
                                IDialogService dialogService,
                                IInspectorService inspectorService,
                                IMessenger messengerService)
        {
            _settingsService = settingsService;
            _projectService = projectService;
            _resourceService = resourceService;
            _dialogService = dialogService;
            _inspectorService = inspectorService;
            _messengerService = messengerService;

            PropertyChanged += ProjectViewModel_PropertyChanged;

            _messengerService.Register<ProjectCreatedMessage>(this, OnProjectCreated);
            _messengerService.Register<ActiveProjectChangedMessage>(this, OnActiveProjectChanged);
            _messengerService.Register<FolderChangedMessage>(this, OnProjectFolderChanged);
            _messengerService.Register<EntityPropertyChangedMessage>(this, OnEntityPropertyChanged);
            _messengerService.Register<PreviouslySelectedEntityMessage>(this, OnPreviouslySelectedEntityMessage);
            _messengerService.Register<SelectedEntityChangedMessage>(this, OnSelectedEntityChanged);
            _messengerService.Register<CelSignatureChangedMessage>(this, OnCelSignatureChanged);
        }

        private void ProjectViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IsProjectSelected))
            {
                if (IsProjectSelected)
                {
                    SelectedResource = null;

                    // Todo: Send a separate message to set the SelectedEntity
                    _inspectorService.SelectedEntity = ActiveProject;
                }
            }
            else if (e.PropertyName == nameof(SelectedResource))
            {
                if (SelectedResource is IEntity entity) 
                { 
                    IsProjectSelected = false;
                    _inspectorService.SelectedEntity = entity;
                }
                else
                {
                    if (_inspectorService.SelectedEntity is Resource)
                    {
                        // Only set the SelectedEntity to null if the previously
                        // selected entity was also a Resource
                        _inspectorService.SelectedEntity = null;
                    }
                }
            }
        }

        private void OnProjectCreated(object recipient, ProjectCreatedMessage message)
        {
            // This will create the project settings as they don't exist yet
            _settingsService.SaveProjectSettings(message.Project.Id);
        }

        private void OnActiveProjectChanged(object recipient, ActiveProjectChangedMessage message)
        {
            var activeProject = message.Project;

            if (activeProject == null)
            {
                ActiveProject = null;
                HasActiveProject = false;
                IsRefreshProjectEnabled = false;
                _settingsService.ClearProjectSettings();
            }
            else
            {
                // Setting the ActiveProject here has the side effect of setting SelectedResource to null,
                // because we don't want to have any resources selected when the project is selected.
                // However, setting SelectedResource to null automatically sets SelectedEntity to null in the ProjectSettings.
                // This is because we always want to persist whatever the latest SelectedEntity is.
                // The problem is, if SelectedEntity is set to null at this point then we can't select the previously selected entity!
                // This needs a redesign to fix properly, but for now we just take a copy of the previously selected entity and 
                // restore after setting the ActiveProject.

                Guard.IsNotNull(_settingsService.ProjectSettings);
                Guid previouslySelectedEntity = _settingsService.ProjectSettings.SelectedEntity;
                ActiveProject = activeProject;
                _settingsService.ProjectSettings.SelectedEntity = previouslySelectedEntity;

                HasActiveProject = true;
            }

            // Stop monitoring previously opened project folder
            _folderWatcher?.Dispose();
            _folderWatcher = null;

            // Todo: We're just using a direct reference to the data in the model
            // should we be making a copy instead?
            // It would make sense to keep a buffer that gets flushed to disk
            // Changes would quickly update the buffer in memory, and minimize save ops

            if (HasActiveProject)
            {
                Guard.IsNotNull(ActiveProject);

                // Listen for changes in the resource registry
                ActiveProject.ResourceRegistry.Root.PropertyChanged -= OnResourcePropertyChanged;
                ActiveProject.ResourceRegistry.Root.PropertyChanged += OnResourcePropertyChanged;

                // Monitor the project folder for changes
                var projectFolder = Path.GetDirectoryName(ActiveProject.ProjectPath);
                Guard.IsNotNull(projectFolder);

                _folderWatcher = new FolderWatcher(_messengerService, projectFolder, 0.25f);
            }
        }

        private void OnSelectedEntityChanged(object recipient, SelectedEntityChangedMessage message)
        {
            if (_settingsService.ProjectSettings == null)
            {
                return;
            }

            _settingsService.ProjectSettings.SelectedEntity = message.Entity == null ? Guid.Empty : message.Entity.Id;
        }

        private void OnCelSignatureChanged(object recipient, CelSignatureChangedMessage message)
        {
            IsRefreshProjectEnabled = true;
        }

        private void OnResourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Entity.Children))
            {
                _projectService.SaveProject();
            }
        }

        public ICommand SelectProjectCommand => new RelayCommand(SelectProject_Executed);
        private void SelectProject_Executed()
        {
            IsProjectSelected = true;
        }

        public ICommand OpenProjectFolderCommand => new RelayCommand(OpenProjectFolder_Executed);
        private void OpenProjectFolder_Executed()
        {
            if (ActiveProject != null)
            {
                var projectPath = ActiveProject.ProjectPath;
                var projectFolder = Path.GetDirectoryName(projectPath);
                Guard.IsNotNull(projectFolder);

                _dialogService.OpenFileExplorer(projectFolder);
            }
        }
        
        public IRelayCommand OpenFolderCommand => new RelayCommand(OpenFolder_Executed);
        private void OpenFolder_Executed()
        {
            if (_projectService.ActiveProject != null)
            {
                var projectPath = _projectService.ActiveProject.ProjectPath;
                var projectFolder = Path.GetDirectoryName(projectPath);
                Guard.IsNotNull(projectFolder);

                _dialogService.OpenFileExplorer(projectFolder);
            }
        }

        public IAsyncRelayCommand AddResourceCommand => new AsyncRelayCommand(AddResource_Executed);
        private async Task AddResource_Executed()
        {
            if (ActiveProject != null)
            {
                await _dialogService.ShowAddResourceDialogAsync();
            }
        }

        public IAsyncRelayCommand RefreshProjectCommand => new AsyncRelayCommand(RefreshProject_Executed);
        private async Task RefreshProject_Executed()
        {
            Guard.IsNotNull(ActiveProject);

            IsRefreshProjectEnabled = false;
            var projectPath = ActiveProject.ProjectPath;

            // LoadProject will close the currently open project first
            var loadResult = await _projectService.LoadProject(projectPath);
            if (loadResult is ErrorResult loadError) 
            {
                Log.Error($"Failed to refresh project. {loadError.Message}");
                return;
            }
        }

        public ICommand CollapseCommand => new RelayCommand(Collapse_Executed);
        private void Collapse_Executed()
        {
            // Toggle the left toolbar expanded state
            Guard.IsNotNull(_settingsService.EditorSettings);
            _settingsService.EditorSettings.LeftPanelExpanded = false;
        }

        private void OnProjectFolderChanged(object recipient, FolderChangedMessage message)
        {
            async void UpdateResources()
            {
                var project = _projectService.ActiveProject;
                Guard.IsNotNull(project);

                var result = await _resourceService.UpdateProjectResources(project);
                if (result.Success)
                {
                    // Save the project if any assets were added or deleted.
                    // There's no need to save the project for changed assets.
                    var summary = result.Data!;
                    if (summary.Added.Count > 0 || summary.Deleted.Count > 0)
                    {
                        _projectService.SaveProject();
                    }
                }
            }
            UpdateResources();
        }

        private void OnEntityPropertyChanged(object recipient, EntityPropertyChangedMessage message)
        {
            var entity = message.Entity;
            if (entity is Project || entity is Resource)
            {
                _projectService.SaveProject();
            }
        }

        private void OnPreviouslySelectedEntityMessage(object recipient, PreviouslySelectedEntityMessage message)
        {
            var entityId = message.EntityId;

            if (ActiveProject != null && ActiveProject.Id == entityId)
            {
                SelectedResource = ActiveProject;
                return;
            }

            var project = _projectService.ActiveProject;
            Guard.IsNotNull(project);

            var result = _resourceService.FindResourceEntity(project, entityId);
            if (result.Success)
            {
                var resourceEntity = result.Data!;
                SelectedResource = resourceEntity;
                return;
            }
        }

        public void DeleteEntity(Entity entity)
        {
            if (entity is Resource resource)
            {
                var project = _projectService.ActiveProject;
                Guard.IsNotNull(project);

                var result = _resourceService.DeleteResource(project, resource);
                if (result.Failure)
                {
                    Log.Error($"Failed to delete resource: {resource.Name}");
                }
                return;
            }

            throw new NotImplementedException();
        }
    }
}
