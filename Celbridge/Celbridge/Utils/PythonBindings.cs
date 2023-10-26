using Celbridge.Services;

namespace Celbridge.Utils
{
    public static class PythonBindings
    {
        public static void ClearHistory()
        {
            var consoleService = (Application.Current as App)!.Host!.Services.GetRequiredService<IConsoleService>();
            consoleService.ClearHistory();
        }

        public static void UpdateResources()
        {
            var resourceService = (Application.Current as App)!.Host!.Services.GetRequiredService<IResourceService>();
            var projectService = (Application.Current as App)!.Host!.Services.GetRequiredService<IProjectService>();

            var activeProject = projectService.ActiveProject;
            Guard.IsNotNull(activeProject);

            _ = resourceService.UpdateProjectResources(activeProject);
        }

        public static void WriteResources()
        {
            var projectService = (Application.Current as App)!.Host!.Services.GetRequiredService<IProjectService>();
            var activeProject = projectService.ActiveProject;

            if (activeProject != null)
            {
                var resourceService = (Application.Current as App)!.Host!.Services.GetRequiredService<IResourceService>();
                resourceService.WriteResourceRegistryToLog(activeProject.ResourceRegistry);
            }
        }

        public static void StartChat(string textFile, string context)
        {
            var consoleService = (Application.Current as App)!.Host!.Services.GetRequiredService<IConsoleService>();
            var resourceService = (Application.Current as App)!.Host!.Services.GetRequiredService<IResourceService>();
            var projectService = (Application.Current as App)!.Host!.Services.GetRequiredService<IProjectService>();

            var project = projectService.ActiveProject;
            if (project == null)
            {
                Log.Error("No project loaded");
                return;
            }

            var pathResult = resourceService.GetPathForNewResource(project, textFile);
            if (pathResult is ErrorResult<string> error)
            {
                Log.Error(error.Message);
                return;
            }
            var textFilePath = pathResult.Data!;

            consoleService.EnterChatMode(textFilePath, context);
        }

        public static void ShowProgressDialog()
        {
            var dialogService = (Application.Current as App)!.Host!.Services.GetRequiredService<IDialogService>();

            async Task PresentProgressDialog()
            {
                dialogService.ShowProgressDialog("Loading Project", null);
                await Task.Delay(5000);
                dialogService.HideProgressDialog();
            }

            _ = PresentProgressDialog();
        }

        public static async void Start()
        {
            var celScriptService = (Application.Current as App)!.Host!.Services.GetRequiredService<ICelScriptService>();
            var projectService = (Application.Current as App)!.Host!.Services.GetRequiredService<IProjectService>();
            var settingsService = (Application.Current as App)!.Host!.Services.GetRequiredService<ISettingsService>();

            if (celScriptService == null || projectService == null)
            {
                return;
            }


            var activeProject = projectService.ActiveProject;
            if (activeProject == null)
            {
                return;
            }

            var projectFolder = activeProject.ProjectFolder;
            var libraryFolder = activeProject.LibraryFolder;

            Guard.IsNotNull(settingsService.EditorSettings);
            var chatAPIKey = settingsService.EditorSettings.OpenAIKey;

            var buildResult = await celScriptService.StartApplication(projectFolder, libraryFolder, chatAPIKey);
            if (buildResult is ErrorResult<string> buildError)
            {
                Log.Error(buildError.Message);
                return;
            }
        }
    }
}
