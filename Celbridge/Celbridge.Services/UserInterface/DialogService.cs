using Celbridge.BaseLibrary.UserInterface;

namespace Celbridge.Services.UserInterface;

public class DialogService : IDialogService
{
    ILoggingService _loggingService;

    public DialogService(ILoggingService loggingService)
    {
        _loggingService = loggingService;
    }

    public async Task ShowAlertAsync(string message)
    {
        await Task.Delay(500);

        _loggingService.Info(message);
    }
}
