using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Commands.Project;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.ScriptUtils.Services;

public class CommonUtils
{
    public static void Alert(string title, string message)
    {
        // UI code must be run on the UI thread
        var dispatcher = ServiceLocator.ServiceProvider.GetRequiredService<IDispatcher>();
        dispatcher.ExecuteAsync(() =>
        {
            var dialogService = ServiceLocator.ServiceProvider.GetRequiredService<IDialogService>();
            dialogService.ShowAlertDialogAsync(title, message, "Ok");
        });
    }

    //
    // Todo: Define these methods in Celbridge.Commands and bind them automically for scripting
    //

    public static void LoadProject(string projectPath)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<ILoadProjectCommand>(command => command.ProjectPath = projectPath);
    }

    public static void UnloadProject()
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IUnloadProjectCommand>();
    }

    public static void CreateProject(string projectName, string folder)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<ICreateProjectCommand>(command =>
        {
            command.Config = new NewProjectConfig(projectName, folder);
        });
    }
}

