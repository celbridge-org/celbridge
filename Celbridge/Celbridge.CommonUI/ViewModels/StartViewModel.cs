using Celbridge.BaseLibrary.Logging;
using CommunityToolkit.Mvvm.Input;

namespace Celbridge.CommonUI.ViewModels;

public partial class StartViewModel : ObservableObject
{
    private ILoggingService _loggingService;

    public StartViewModel(ILoggingService loggingService)
    {
        _loggingService = loggingService;
    }

    public ICommand SelectNewUICommand => new RelayCommand(SelectNewUICommand_Executed);
    private void SelectNewUICommand_Executed()
    {
        _loggingService.Info("New UI");        
    }

    public ICommand SelectLegacyUICommand => new RelayCommand(SelectLegacyUICommand_Executed);
    private void SelectLegacyUICommand_Executed()
    {
        _loggingService.Info("Legacy UI");
    }
}

