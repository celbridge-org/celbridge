namespace Celbridge.UserInterface.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly Logging.ILogger<MainPageViewModel> _logger;

    public MainPageViewModel(
        Logging.ILogger<MainPageViewModel> logger,
        IMessengerService messengerService)
    {
        _logger = logger;
        _messengerService = messengerService;
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
}
