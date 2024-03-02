using Celbridge.BaseLibrary.UserInterface;
using Celbridge.CommonViewModels.Pages;
using Celbridge.CommonViews.UserControls;

namespace Celbridge.CommonViews.Pages;

public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; private set; }

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
        var navigationService = serviceProvider.GetRequiredService<INavigationService>();

#if WINDOWS
        // Setup the custom title bar (Windows only)
        var titleBar = serviceProvider.GetRequiredService<TitleBar>();
        LayoutRoot.Children.Add(titleBar);
        var mainWindow = navigationService.MainWindow as Window;
        mainWindow!.ExtendsContentIntoTitleBar = true;
        mainWindow!.SetTitleBar(titleBar);
#endif

        ViewModel.OnNavigate += OnViewModel_Navigate;
        ViewModel.OnMainPage_Loaded();

        // Begin listening for user navigation events
        MainNavigation.ItemInvoked += OnMainPage_NavigationViewItemInvoked;
    }

    private void OnMainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Unregister all event handlers to avoid memory leaks

        ViewModel.OnNavigate -= OnViewModel_Navigate;

        MainNavigation.ItemInvoked -= OnMainPage_NavigationViewItemInvoked;

        Loaded -= OnMainPage_Loaded;
        Unloaded -= OnMainPage_Unloaded;
    }

    private Result OnViewModel_Navigate(Type pageType)
    {
        if (ContentFrame.Content != null &&
            ContentFrame.Content.GetType() == pageType)
        {
            // Already at the requested page, so just early out.
            return Result.Ok();
        }

        if (ContentFrame.Navigate(pageType))
        {
            return Result.Ok();
        }
        return Result.Fail($"Failed to navigate to page type {pageType}");
    }

    private void OnMainPage_NavigationViewItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            ViewModel.SelectNavigationItem(MainPageViewModel.SettingsPageName);
            return;
        }

        var item = args.InvokedItemContainer as NavigationViewItem;
        Guard.IsNotNull(item);

        var navigationItemTag = item.Tag;
        Guard.IsNotNull(navigationItemTag);

        var pageName = navigationItemTag.ToString();
        Guard.IsNotNullOrEmpty(pageName);

        ViewModel.SelectNavigationItem(pageName);
    }

    public void Navigate(Type pageType)
    {
        Guard.IsNotNull(ContentFrame);

        if (ContentFrame.Content is null ||
            ContentFrame.Content.GetType() != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }
}
