using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.UserInterface;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Celbridge.Console.ViewModels;

public partial class ConsolePanelViewModel : ObservableObject
{
    private readonly IConsoleService _consoleService;

    [ObservableProperty]
    private string _inputText = string.Empty;

    private ObservableCollection<string> _outputItems = new ();
    public ObservableCollection<string> OutputItems
    {
        get => _outputItems;
        set
        {
            _outputItems = value;
            OnPropertyChanged(nameof(OutputItems));
        }
    }

    public ConsolePanelViewModel(
        IUserInterfaceService userInterfaceService, 
        IConsoleService consoleService)
    {
        _consoleService = consoleService;  // Transient instance created via DI

        // Register the console service with the workspace service
        userInterfaceService.WorkspaceService.RegisterService(_consoleService);
    }

    public ICommand ClearCommand => new RelayCommand(Clear_Executed);
    private void Clear_Executed()
    {
        _outputItems.Clear();
    }

    public ICommand SubmitCommand => new RelayCommand(Submit_Executed);
    private void Submit_Executed()
    {
        _outputItems.Add(InputText);

        InputText = string.Empty;
    }
}
