using Celbridge.BaseLibrary.Console;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;

namespace Celbridge.Shell.ViewModels;

public partial class WorkspaceViewModel : ObservableObject
{
    private IConsoleService _consoleService;

    public WorkspaceViewModel(IConsoleService consoleService)
    {
        _consoleService = consoleService;
    }

    [ObservableProperty]
    public string _message = string.Empty;

    public ICommand UpdateText => new RelayCommand(UpdateText_Executed);
    private void UpdateText_Executed()
    {
        Message = _consoleService.GetTestString();
    }
}
