using Celbridge.CommonUI.UserInterface;

namespace Celbridge.CommonUI.Views;

public sealed partial class MainPage : Page
{
    private readonly IUserInterfaceService _userInterfaceService;

    public MainPageViewModel ViewModel { get; private set; }

    public void Navigate(Type page)
    {
        ContentFrame.Navigate(page);
    }

    public MainPage()
    {
        this.InitializeComponent();

        var serviceProvider = Services.ServiceProvider;

        _userInterfaceService = serviceProvider.GetRequiredService<IUserInterfaceService>();

        ViewModel = serviceProvider.GetRequiredService<MainPageViewModel>();

        Loaded += OnMainPage_Loaded;
        Unloaded += OnMainPage_Unloaded;
    }

    private void OnMainPage_Loaded(object sender, RoutedEventArgs e)
    {
#if WINDOWS
        // Setup the custom title bar (Windows only)

        var serviceProvider = Services.ServiceProvider;
        var titleBar = serviceProvider.GetRequiredService<TitleBar>();
        LayoutRoot.Children.Add(titleBar);

        var mainWindow = _userInterfaceService.MainWindow;
        mainWindow.ExtendsContentIntoTitleBar = true;
        mainWindow.SetTitleBar(titleBar);
#endif

        Navigate(typeof(StartView));
    }

    private void OnMainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Unregister all event handlers to avoid memory leaks

        Loaded -= OnMainPage_Loaded;
        Unloaded -= OnMainPage_Unloaded;
    }
}
