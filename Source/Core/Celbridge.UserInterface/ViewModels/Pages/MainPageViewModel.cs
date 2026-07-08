namespace Celbridge.UserInterface.ViewModels.Pages;

public partial class MainPageViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly Logging.ILogger<MainPageViewModel> _logger;
    private readonly IUndoService _undoService;

    public MainPageViewModel(
        Logging.ILogger<MainPageViewModel> logger,
        IMessengerService messengerService,
        IUndoService undoService)
    {
        _logger = logger;
        _messengerService = messengerService;
        _undoService = undoService;

        // Register for undo/redo messages
        _messengerService.Register<UndoRequestedMessage>(this, OnUndoRequested);
        _messengerService.Register<RedoRequestedMessage>(this, OnRedoRequested);
    }

    public void OnMainPage_Loaded()
    {
        // The app activation service opens the startup project in response to this message.
        _messengerService.Send(new MainPageLoadedMessage());
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
