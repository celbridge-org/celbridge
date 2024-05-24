using Celbridge.BaseLibrary.Tasks;
using Celbridge.BaseLibrary.UserInterface;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.ScriptUtils;

public class CommonUtils
{
    public class ShowAlert() : ITask
    {
        public async Task<Result> ExecuteAsync(CancellationToken cancellationToken)
        {
            var userInterfaceService = ServiceLocator.ServiceProvider.GetRequiredService<IUserInterfaceService>();
            var dialogService = userInterfaceService.DialogService;

            await dialogService.ShowAlertDialogAsync("1", "2", "Ok");

            return Result.Ok();
        }
    }

    public static void Alert(string title, string message)
    {
        // Is this somehow not getting called on the main thread?
        // Try somthing simple, like writing to the console service
        // Isn't there somehting weird about show dialog - has to be called from the main thread or something
        // Hang on - doesn't the scheduler start once there's a task - maybe it's using the wrong thread?

        var schedulerService = ServiceLocator.ServiceProvider.GetRequiredService<ISchedulerService>();
        schedulerService.ScheduleTask(new ShowAlert());
    }
}

