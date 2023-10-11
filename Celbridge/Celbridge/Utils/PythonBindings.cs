using Celbridge.Services;
using Celbridge.Tasks;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Serilog;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace Celbridge.Utils
{
    public static class PythonBindings
    {
        public static void ClearHistory()
        {
            var consoleService = (Application.Current as App).Host.Services.GetService<IConsoleService>();
            consoleService.ClearHistory();
        }

        public static void UpdateResources()
        {
            var resourceService = (Application.Current as App).Host.Services.GetService<IResourceService>();
            var projectService = (Application.Current as App).Host.Services.GetService<IProjectService>();

            var activeProject = projectService.ActiveProject;
            _ = resourceService.UpdateProjectResources(activeProject);
        }

        public static void WriteResources()
        {
            var projectService = (Application.Current as App).Host.Services.GetService<IProjectService>();
            var activeProject = projectService.ActiveProject;

            if (activeProject != null)
            {
                var resourceService = (Application.Current as App).Host.Services.GetService<IResourceService>();
                resourceService.WriteResourceRegistryToLog(activeProject.ResourceRegistry);
            }
        }

        public static void StartChat(string textFile)
        {
            var consoleService = (Application.Current as App).Host.Services.GetService<IConsoleService>();
            var resourceService = (Application.Current as App).Host.Services.GetService<IResourceService>();
            var projectService = (Application.Current as App).Host.Services.GetService<IProjectService>();

            var project = projectService.ActiveProject;
            if (project == null)
            {
                Log.Error("No project loaded");
                return;
            }

            var pathResult = resourceService.GetPathForNewResource(project, textFile);
            if (pathResult.Failure)
            {
                var error = pathResult as ErrorResult<string>;
                Log.Error(error.Message);
                return;
            }
            var textFilePath = pathResult.Data;

            consoleService.EnterChatMode(textFilePath);
        }

        public static void ShowProgressDialog()
        {
            var dialogService = (Application.Current as App).Host.Services.GetService<IDialogService>();

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
            var celScriptService = (Application.Current as App).Host.Services.GetService<ICelScriptService>();
            var projectService = (Application.Current as App).Host.Services.GetService<IProjectService>();

            if (celScriptService == null || projectService == null)
            {
                return;
            }

            var activeProject = projectService.ActiveProject;
            if (activeProject == null)
            {
                return;
            }

            var libraryFolder = activeProject.LibraryFolder;
            var buildResult = await celScriptService.StartApplication(libraryFolder);
            if (buildResult is ErrorResult<string> buildError)
            {
                Log.Error(buildError.Message);
                return;
            }
        }
    }
}
