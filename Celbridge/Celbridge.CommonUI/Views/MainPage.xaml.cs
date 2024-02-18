using Celbridge.CommonUI.Messages;
using Celbridge.CommonUI.UserInterface;

namespace Celbridge.CommonUI.Views;

public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; private set; }

    public void Navigate(Type pageType)
    {
        Guard.IsNotNull(ContentFrame);

        if (ContentFrame.Content is null ||
            ContentFrame.Content.GetType() != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    public MainPage()
    {
        this.InitializeComponent();

        var serviceProvider = Services.ServiceProvider;
        ViewModel = serviceProvider.GetRequiredService<MainPageViewModel>();

        Loaded += OnMainPage_Loaded;
        Unloaded += OnMainPage_Unloaded;
    }

    private void OnMainPage_Loaded(object sender, RoutedEventArgs e)
    {
        var serviceProvider = Services.ServiceProvider;
        var userInterfaceService = serviceProvider.GetRequiredService<IUserInterfaceService>();

#if WINDOWS
        // Setup the custom title bar (Windows only)
        var titleBar = serviceProvider.GetRequiredService<TitleBar>();
        LayoutRoot.Children.Add(titleBar);
        var mainWindow = userInterfaceService.MainWindow;
        mainWindow.ExtendsContentIntoTitleBar = true;
        mainWindow.SetTitleBar(titleBar);
#endif

        // Notify listeners that the Main Page has now loaded so page navigation can be performed now.
        var messengerService = serviceProvider.GetRequiredService<IMessengerService>();
        var message = new MainPageLoadedMessage(this);
        messengerService.Send(message);

        // Navigate to the initial page
        Navigate(typeof(StartView));

        // Begin listening for user navigation events
        MainNavigation.ItemInvoked += OnMainPage_NavigationViewItemInvoked;
    }

    private void OnMainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Unregister all event handlers to avoid memory leaks

        MainNavigation.ItemInvoked -= OnMainPage_NavigationViewItemInvoked;

        Loaded -= OnMainPage_Loaded;
        Unloaded -= OnMainPage_Unloaded;
    }

    private void OnMainPage_NavigationViewItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            // _userInterfaceService.Navigate<SettingsPage>();
            return;
        }

        var item = args.InvokedItemContainer as NavigationViewItem;
        Guard.IsNotNull(item);

        switch (item.Tag.ToString())
        {
            case "MainPage.Home":
                ViewModel.SelectNavigationItem_Home();
                break;
            case "MainPage.NewProject":
                ViewModel.SelectNavigationItem_NewProject();
                break;
            case "MainPage.OpenProject":
                ViewModel.SelectNavigationItem_OpenProject();
                break;
        }
    }
}
