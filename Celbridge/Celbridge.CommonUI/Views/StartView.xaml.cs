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

        Loaded += StartView_Loaded;
        Unloaded += StartView_Unloaded;
    }

    private void StartView_Loaded(object sender, RoutedEventArgs e)
    {
#if WINDOWS
        var titleBar = new TitleBar();
        LayoutRoot.Children.Add(titleBar);

        // Extend the application content into the title bar area
        var mainWindow = _userInterfaceService.MainWindow;
        mainWindow.ExtendsContentIntoTitleBar = true;
        mainWindow.SetTitleBar(titleBar);
#endif
    }

    private void StartView_Unloaded(object sender, RoutedEventArgs e)
    {
        //
        // Unregister all event handlers to avoid memory leaks
        //

        Loaded -= StartView_Loaded;
        Unloaded -= StartView_Unloaded;
    }
}
