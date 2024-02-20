using CommunityToolkit.Mvvm.Input;

namespace Celbridge.CommonUI.ViewModels;
public class MainMenuViewModel : ObservableObject
{
    public MainMenuViewModel()
    {}

    public ICommand NewProjectCommand => new RelayCommand(NewProject_Executed);
    private void NewProject_Executed()
    {
        throw new NotImplementedException();
    }

    public ICommand OpenProjectCommand => new RelayCommand(OpenProject_Executed);
    private void OpenProject_Executed()
    {
        throw new NotImplementedException();
    }

    public ICommand CloseProjectCommand => new RelayCommand(CloseProject_Executed);
    private void CloseProject_Executed()
    {
        throw new NotImplementedException();
    }

    public ICommand OpenSettingsCommand => new RelayCommand(OpenSettings_Executed);
    private void OpenSettings_Executed()
    {
        throw new NotImplementedException();
    }

    public ICommand ExitCommand => new RelayCommand(Exit_Executed);
    private void Exit_Executed()
    {
#if !HAS_UNO
        Application.Current.Exit();
#endif
    }
}
