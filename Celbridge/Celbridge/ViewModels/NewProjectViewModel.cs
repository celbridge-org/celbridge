using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using Windows.Storage;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;
using Serilog;
using Celbridge.Services;
using Celbridge.Utils;

namespace Celbridge.ViewModels
{
    public partial class NewProjectViewModel : ObservableObject
    {
        private readonly ISettingsService _settingsService;
        private readonly IProjectService _projectService;

        private string _projectName;
        public string ProjectName
        {
            get { return _projectName; }
            set
            {
                SetProperty(ref _projectName, value);
            }
        }

        private string _projectFolder;
        public string ProjectFolder
        {
            get { return _projectFolder; }
            set
            {
                SetProperty(ref _projectFolder, value);
            }
        }

        [ObservableProperty]
        private bool _canCreateProject;

        public ICommand PickFolderCommand => new AsyncRelayCommand(PickFolder_Executed);
        public ICommand CreateProjectCommand => new AsyncRelayCommand(CreateProject_Executed);

        public NewProjectViewModel(ISettingsService settingsService, IProjectService projectService)
        {
            _settingsService = settingsService;
            _projectService = projectService;

            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ProjectName) || e.PropertyName == nameof(ProjectFolder))
                {
                    CanCreateProject = _projectService.GetProjectPath(ProjectFolder, ProjectName).Success;
                }
            };

            // There's no cross-platform way to get the path to the documents folder on Uno Platform.
            // The best we can do is persist the most recent folder that the user picked.

            ProjectFolder = _settingsService.EditorSettings.PreviousNewProjectFolder;
        }

        private async Task PickFolder_Executed()
        {
            var result = await FileUtils.ShowFolderPicker();
            if (result.Success)
            {
                ProjectFolder = result.Data.Path;
            }
        }

        private async Task CreateProject_Executed()
        {
            var createResult = await _projectService.CreateProject(ProjectFolder, ProjectName);
            if (createResult.Failure)
            {
                var error = createResult as ErrorResult<string>;
                Log.Information($"Failed to create `{ProjectName}` in `{ProjectFolder}. {error.Message}");
                return;
            }

            // Remember project folder for next time
            _settingsService.EditorSettings.PreviousNewProjectFolder = ProjectFolder;

            var projectPath = createResult.Data;

            var openResult = await _projectService.LoadProject(projectPath);
            if (openResult.Success)
            {
                Log.Information($"Created `{ProjectName}` at `{projectPath}");
            }
            else
            {
                throw new InvalidOperationException($"Failed to open newly created project at: {projectPath}");
            }
        }
    }
}
