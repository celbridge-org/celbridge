using Celbridge.UserInterface;

namespace Celbridge.UserInterface.Views;

public sealed partial class TitleBar : UserControl
{
    private readonly IMessengerService _messengerService;
    private IStringLocalizer _stringLocalizer;

    public LocalizedString ApplicationNameString => _stringLocalizer.GetString("ApplicationName");

    public TitleBar()
    {
        InitializeComponent();

        var serviceProvider = ServiceLocator.ServiceProvider;
        _messengerService = serviceProvider.GetRequiredService<IMessengerService>();
        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        Loaded += OnTitleBar_Loaded;
        Unloaded += OnTitleBar_Unloaded;
    }

    private void OnTitleBar_Loaded(object sender, RoutedEventArgs e)
    {
        _messengerService.Register<MainWindowActivatedMessage>(this, OnMainWindowActivated);
        _messengerService.Register<MainWindowDeactivatedMessage>(this, OnMainWindowDeactivated);
    }

    private void OnTitleBar_Unloaded(object sender, RoutedEventArgs e)
    {
        // Unregister all event handlers to avoid memory leaks

        Loaded -= OnTitleBar_Loaded;
        Unloaded -= OnTitleBar_Unloaded;

        _messengerService.Unregister<MainWindowActivatedMessage>(this);
        _messengerService.Unregister<MainWindowDeactivatedMessage>(this);
    }

    private void OnMainWindowActivated(object recipient, MainWindowActivatedMessage message)
    {
        VisualStateManager.GoToState(this, "Active", false);
    }

    private void OnMainWindowDeactivated(object recipient, MainWindowDeactivatedMessage message)
    {
        VisualStateManager.GoToState(this, "Inactive", false);
    }
}
