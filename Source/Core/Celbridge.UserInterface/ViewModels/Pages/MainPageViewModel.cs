using Celbridge.Commands;
using Celbridge.Navigation;
using Celbridge.Projects;
using Celbridge.Settings;

namespace Celbridge.UserInterface.ViewModels.Pages;

public partial class MainPageViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly Logging.ILogger<MainPageViewModel> _logger;
    private readonly INavigationService _navigationService;
    private readonly ICommandService _commandService;
    private readonly ISettingsService _settingsService;
    private readonly IUndoService _undoService;
    private readonly ILocalFileSystem _fileSystem;

    public MainPageViewModel(
        Logging.ILogger<MainPageViewModel> logger,
        IMessengerService messengerService,
        INavigationService navigationService,
        ICommandService commandService,
        ISettingsService settingsService,
        IUndoService undoService,
        ILocalFileSystem fileSystem)
    {
        _logger = logger;
        _messengerService = messengerService;
        _navigationService = navigationService;
        _commandService = commandService;
        _settingsService = settingsService;
        _undoService = undoService;
        _fileSystem = fileSystem;

        // Register for undo/redo messages
        _messengerService.Register<UndoRequestedMessage>(this, OnUndoRequested);
        _messengerService.Register<RedoRequestedMessage>(this, OnRedoRequested);
    }

    public async Task OnMainPage_LoadedAsync()
    {
        // Open the previous project if one was loaded last time we ran the application.
        var previousProjectFile = _settingsService.Get(SettingCatalog.Project.PreviousProject);
        bool previousProjectExists = false;
        if (!string.IsNullOrEmpty(previousProjectFile))
        {
            var infoResult = await _fileSystem.GetInfoAsync(previousProjectFile);
            previousProjectExists = infoResult.IsSuccess
                && infoResult.Value.Kind == StorageItemKind.File;
        }

        if (previousProjectExists)
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
    {
        _messengerService.UnregisterAll(this);
    }

    private void OnUndoRequested(object recipient, UndoRequestedMessage message)
    {
        _undoService.Undo();
    }

    private void OnRedoRequested(object recipient, RedoRequestedMessage message)
    {
        _undoService.Redo();
    }

    public void Undo()
    {
        _undoService.Undo();
    }

    public void Redo()
    {
        _undoService.Redo();
    }
}

