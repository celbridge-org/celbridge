using Celbridge.CommonUI.UserInterface;

namespace Celbridge.CommonUI.Views;

public sealed partial class StartView : Page
{
    private readonly IUserInterfaceService _userInterfaceService;

    public StartViewModel ViewModel { get; private set; }

    public StartView()
    {
        this.InitializeComponent();

        var serviceProvider = BaseLibrary.Core.Services.ServiceProvider;

        _userInterfaceService = serviceProvider.GetRequiredService<IUserInterfaceService>();

        ViewModel = serviceProvider.GetRequiredService<StartViewModel>();

        Loaded += OnStartView_Loaded;
        Unloaded += OnStartView_Unloaded;
    }

    private void OnStartView_Loaded(object sender, RoutedEventArgs e)
    {
#if WINDOWS
        // Setup the custom title bar (Windows only)

        var serviceProvider = BaseLibrary.Core.Services.ServiceProvider;
        var titleBar = serviceProvider.GetRequiredService<TitleBar>();
        LayoutRoot.Children.Add(titleBar);

        var mainWindow = _userInterfaceService.MainWindow;
        mainWindow.ExtendsContentIntoTitleBar = true;
        mainWindow.SetTitleBar(titleBar);
#endif
    }

    private void OnStartView_Unloaded(object sender, RoutedEventArgs e)
    {
        // Unregister all event handlers to avoid memory leaks

        Loaded -= OnStartView_Loaded;
        Unloaded -= OnStartView_Unloaded;
    }
}
