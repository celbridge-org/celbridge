using Celbridge.Commands;
using Celbridge.Navigation;
using Celbridge.Projects;
using Celbridge.Settings;
using Celbridge.UserInterface.Services;

namespace Celbridge.UserInterface.ViewModels.Pages;

public partial class MainPageViewModel : ObservableObject, INavigationProvider
{
    private readonly IMessengerService _messengerService;
    private readonly Logging.ILogger<MainPageViewModel> _logger;
    private readonly INavigationService _navigationService;
    private readonly ICommandService _commandService;
    private readonly IEditorSettings _editorSettings;
    private readonly IUndoService _undoService;

    public MainPageViewModel(
        Logging.ILogger<MainPageViewModel> logger,
        IMessengerService messengerService,
        INavigationService navigationService,
        ICommandService commandService,
        IEditorSettings editorSettings,
        IUndoService undoService)
    {
        _logger = logger;
        _messengerService = messengerService;
        _navigationService = navigationService;
        _commandService = commandService;
        _editorSettings = editorSettings;
        _undoService = undoService;
    }

    public event Func<Type, object, Result>? OnNavigate;

    public delegate string ReturnCurrentPageDelegate();

    public ReturnCurrentPageDelegate ReturnCurrentPage = () => string.Empty;

    public Result NavigateToPage(Type pageType)
    {
        // Pass the empty string to avoid making the parameter nullable.
        return NavigateToPage(pageType, string.Empty);
    }

    public Result NavigateToPage(Type pageType, object parameter)
    {
        return OnNavigate?.Invoke(pageType, parameter)!;
    }

    public Result SelectNavigationItemByNavigationTag(string navigationTag)
    {
        // This is now handled by the TitleBar navigation directly
        return Result.Ok();
    }

    public string GetCurrentPageName()
    {
        return ReturnCurrentPage();
    }

    public void OnMainPage_Loaded()
    {
        // Register this class as the navigation provider for the application
        var navigationService = _navigationService as NavigationService;
        Guard.IsNotNull(navigationService);
        navigationService.SetNavigationProvider(this);

        // Open the previous project if one was loaded last time we ran the application.
        var previousProjectFile = _editorSettings.PreviousProject;
        if (!string.IsNullOrEmpty(previousProjectFile) &&
            File.Exists(previousProjectFile))
        { 
            _commandService.Execute<ILoadProjectCommand>((command) =>
            {
                command.ProjectFilePath = previousProjectFile;
            });
        }
        else
        {
            // No previous project to load, so navigate to the home page
            _navigationService.NavigateToPage(NavigationConstants.HomeTag);
        }
    }

    public void OnMainPage_Unloaded()
    { }

    public void Undo()
    {
        _undoService.Undo();
    }

    public void Redo()
    {
        _undoService.Redo();
    }
}

