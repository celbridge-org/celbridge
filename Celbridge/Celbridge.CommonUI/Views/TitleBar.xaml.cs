using Celbridge.CommonUI.Messages;

namespace Celbridge.CommonUI.Views;

public sealed partial class TitleBar : UserControl
{
    private readonly IMessengerService _messengerService;

    public TitleBar(IMessengerService messengerService)
    {
        InitializeComponent();

        _messengerService = messengerService;

        Loaded += OnTitleBar_Loaded;
        Unloaded += OnTitleBar_Unloaded;
    }

    private void OnTitleBar_Loaded(object sender, RoutedEventArgs e)
    {
        _messengerService.Register<MainWindowActivated>(this, OnMainWindowActivated);
        _messengerService.Register<MainWindowDeactivated>(this, OnMainWindowDeactivated);
    }

    private void OnTitleBar_Unloaded(object sender, RoutedEventArgs e)
    {
        // Unregister all event handlers to avoid memory leaks

        Loaded -= OnTitleBar_Loaded;
        Unloaded -= OnTitleBar_Unloaded;

        _messengerService.Unregister<MainWindowActivated>(this);
        _messengerService.Unregister<MainWindowDeactivated>(this);
    }

    private void OnMainWindowActivated(object recipient, MainWindowActivated message)
    {
        VisualStateManager.GoToState(this, "Active", false);
    }

    private void OnMainWindowDeactivated(object recipient, MainWindowDeactivated message)
    {
        VisualStateManager.GoToState(this, "Inactive", false);
    }
}
