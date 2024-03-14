namespace Celbridge.Console.ViewModels;

public class ConsolePanelViewModel
{
    ILoggingService _loggingService;

    public ConsolePanelViewModel(ILoggingService loggingService)
    {
        _loggingService = loggingService;
    }

    public ICommand ClearCommand => new RelayCommand(Clear_Executed);
    private void Clear_Executed()
    {
        _loggingService.Info("Clear");
    }
}
