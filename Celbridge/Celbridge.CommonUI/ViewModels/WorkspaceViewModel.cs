using Celbridge.BaseLibrary.Logging;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;

namespace Celbridge.Shell.ViewModels;

public class WorkspaceViewModel
{
    private ILoggingService _loggingService;

    public WorkspaceViewModel(ILoggingService loggingService)
    {
        _loggingService = loggingService;
    }

    public ICommand PrintText => new RelayCommand(PrintText_Executed);
    private void PrintText_Executed()
    {
        _loggingService.Info("Some text");
    }
}
