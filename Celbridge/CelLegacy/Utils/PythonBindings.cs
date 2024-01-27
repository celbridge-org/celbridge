namespace Celbridge.Legacy.Utils;

public static class PythonBindings
{
    public static void ClearHistory()
    {
        var consoleService = LegacyServiceProvider.Services!.GetRequiredService<IConsoleService>();
        consoleService.ClearHistory();
    }

    public static void UpdateResources()
    {
        var resourceService = LegacyServiceProvider.Services!.GetRequiredService<IResourceService>();
        var projectService = LegacyServiceProvider.Services!.GetRequiredService<IProjectService>();

        var activeProject = projectService.ActiveProject;
        Guard.IsNotNull(activeProject);

        _ = resourceService.UpdateProjectResources(activeProject);
    }

    public static void WriteResources()
    {
        var projectService = LegacyServiceProvider.Services!.GetRequiredService<IProjectService>();
        var activeProject = projectService.ActiveProject;

        if (activeProject != null)
        {
            var resourceService = LegacyServiceProvider.Services!.GetRequiredService<IResourceService>();
            resourceService.WriteResourceRegistryToLog(activeProject.ResourceRegistry);
        }
    }

    public static void StartChat(string textFile, string context)
    {
        var consoleService = LegacyServiceProvider.Services!.GetRequiredService<IConsoleService>();
        var resourceService = LegacyServiceProvider.Services!.GetRequiredService<IResourceService>();
        var projectService = LegacyServiceProvider.Services!.GetRequiredService<IProjectService>();

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
        var dialogService = LegacyServiceProvider.Services!.GetRequiredService<IDialogService>();

        async Task PresentProgressDialog()
        {
            dialogService.ShowProgressDialog("Loading Project", null);
            await Task.Delay(5000);
            dialogService.HideProgressDialog();
        }

        _ = PresentProgressDialog();
    }
}
