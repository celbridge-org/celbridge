using Celbridge.Services;
using Celbridge.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Serilog;
using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Uno.Extensions;
using Windows.Storage.Pickers;

namespace Celbridge.ViewModels
{
    public class MainMenuViewModel : ObservableObject
    {
        private readonly IDialogService _dialogService;
        private readonly IProjectService _projectService;

        public MainMenuViewModel(IDialogService dialogService, IProjectService projectService)
        {
            _dialogService = dialogService;
            _projectService = projectService;
        }

        public ICommand NewProjectCommand => new AsyncRelayCommand(NewProject_Executed);
        private async Task NewProject_Executed()
        {
            await _dialogService.ShowNewProjectDialogAsync();
        }

        public ICommand OpenProjectCommand => new AsyncRelayCommand(OpenProject_Executed);
        private async Task OpenProject_Executed()
        {
            // Create a new FileOpenPicker
            var filePicker = new FileOpenPicker();

            // Set the file type filters
            filePicker.FileTypeFilter.Add(Constants.ProjectFileExtension);

#if WINDOWS
            // For Uno.WinUI-based apps
            var mainWindow = (Application.Current as App).MainWindow;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(filePicker, hwnd);
#endif

            // Show the picker and wait for the user to select a file
            var projectFile = await filePicker.PickSingleFileAsync();
            // If the user selected a file, return the file path
            if (projectFile != null)
            {
                var projectPath = projectFile.Path;
                await _projectService.LoadProject(projectPath);
                Log.Information($"Opened project: {projectPath}");
            }
        }

        public ICommand CloseProjectCommand => new AsyncRelayCommand(CloseProject_Executed);
        private async Task CloseProject_Executed()
        {
            var result = await _projectService.CloseProject();
            if (result.Success)
            {
                Log.Information($"Closed project");
            }
            else if (result is ErrorResult error)
            {
                Log.Information(error.Message);
            }
        }

        public ICommand OpenSettingsCommand => new AsyncRelayCommand(OpenSettings_Executed);
        private async Task OpenSettings_Executed()
        {
            await _dialogService.ShowSettingsDialogAsync();
        }

        public ICommand ExitCommand => new RelayCommand(Exit_Executed);
        private void Exit_Executed()
        {
#if !MACCATALYST
            Application.Current.Exit();
#endif
        }
    }
}
