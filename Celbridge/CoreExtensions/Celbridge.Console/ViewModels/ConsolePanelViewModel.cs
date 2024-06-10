using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Console.ViewModels;

public partial class ConsolePanelViewModel : ObservableObject
{
    public event Action? OnClearConsole;
    public event Action? OnAddConsoleTab;

    public ConsolePanelViewModel()
    {}

    public ICommand ClearCommand => new RelayCommand(ClearCommand_Executed);
    private void ClearCommand_Executed()
    {
        OnClearConsole?.Invoke();
    }

    public ICommand AddConsoleTabCommand => new RelayCommand(AddConsoleTabCommand_Executed);
    private void AddConsoleTabCommand_Executed()
    {
        OnAddConsoleTab?.Invoke();
    }
}
