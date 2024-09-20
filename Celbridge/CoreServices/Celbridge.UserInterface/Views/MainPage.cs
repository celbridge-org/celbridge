using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace Celbridge.UserInterface.Views;

public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; private set; }

    public LocalizedString HomeString => _stringLocalizer.GetString($"MainPage_Home");
    public LocalizedString NewProjectString => _stringLocalizer.GetString($"MainPage_NewProject");
    public LocalizedString OpenProjectString => _stringLocalizer.GetString($"MainPage_OpenProject");
    public LocalizedString CloseProjectString => _stringLocalizer.GetString($"MainPage_CloseProject");

    private IStringLocalizer _stringLocalizer;
    private IUserInterfaceService _userInterfaceService;

    private Grid _layoutRoot;
    private NavigationView _mainNavigation;
    private Frame _contentFrame;

    public MainPage()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();
        _userInterfaceService = serviceProvider.GetRequiredService<IUserInterfaceService>();

        ViewModel = serviceProvider.GetRequiredService<MainPageViewModel>();

        _contentFrame = new Frame()
            .Background(StaticResource.Get<Brush>("ApplicationBackgroundBrush"))
            .Name("ContentFrame");

        _mainNavigation = new NavigationView()
            .Name("MainNavigation")
            .Grid(row: 1)
            .Background(StaticResource.Get<Brush>("PanelBackgroundABrush"))
            .IsBackButtonVisible(NavigationViewBackButtonVisible.Collapsed)
            .PaneDisplayMode(NavigationViewPaneDisplayMode.LeftMinimal)
            .MenuItems(
                new NavigationViewItem()
                    .Icon(new SymbolIcon(Symbol.Home))
                    .Tag(MainPageViewModel.HomeTag)
                    .Content(HomeString),
                new NavigationViewItem()
                    .Icon(new SymbolIcon(Symbol.NewFolder))
                    .Tag(MainPageViewModel.NewProjectTag)
                    .Content(NewProjectString),
                new NavigationViewItem()
                    .Icon(new SymbolIcon(Symbol.OpenFile))
                    .Tag(MainPageViewModel.OpenProjectTag)
                    .Content(OpenProjectString)
                )
                .Content(_contentFrame);

        _layoutRoot = new Grid()
            .Name("LayoutRoot")
            .RowDefinitions("Auto, *")
            .Children(_mainNavigation);

        this.DataContext(ViewModel, (page, vm) => page
            .Content(_layoutRoot));

        Loaded += OnMainPage_Loaded;
        Unloaded += OnMainPage_Unloaded;
    }

    private void OnMainPage_Loaded(object sender, RoutedEventArgs e)
    {
        var mainWindow = _userInterfaceService.MainWindow as Window;
        Guard.IsNotNull(mainWindow);

#if WINDOWS
        // WinUI displays a light title bar in dark mode by default. 
        // This looks terrible, so we override the title bar in dark mode to use a dark version.
        if (_userInterfaceService.UserInterfaceTheme == UserInterfaceTheme.Dark)
        {
            // Setup the custom title bar (Windows only)
            var titleBar = new TitleBar();
            _layoutRoot.Children.Add(titleBar);

            mainWindow.ExtendsContentIntoTitleBar = true;
            mainWindow.SetTitleBar(titleBar);
        }
#endif

        ViewModel.OnNavigate += OnViewModel_Navigate;
        ViewModel.OnMainPage_Loaded();

        // Begin listening for user navigation events
        _mainNavigation.ItemInvoked += OnMainPage_NavigationViewItemInvoked;

        // Listen for keyboard input events
#if WINDOWS
        mainWindow.Content.KeyDown += (s, e) =>
        {
            if (OnKeyDown(e.Key))
            {
                e.Handled = true;
            }
        };
#else
        Guard.IsNotNull(mainWindow);
        Guard.IsNotNull(mainWindow.CoreWindow);
        mainWindow.CoreWindow.KeyDown += (s, e) =>
        {
            if (OnKeyDown(e.VirtualKey))
            {
                e.Handled = true;
            }
        };
#endif
    }

    private void OnMainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnMainPage_Unloaded();

        // Unregister all event handlers to avoid memory leaks

        ViewModel.OnNavigate -= OnViewModel_Navigate;

        _mainNavigation.ItemInvoked -= OnMainPage_NavigationViewItemInvoked;

        Loaded -= OnMainPage_Loaded;
        Unloaded -= OnMainPage_Unloaded;
    }

    private bool OnKeyDown(VirtualKey key)
    {
        // Use the HasFlag method to check if the control key is down.
        // If you just compare with CoreVirtualKeyStates.Down it doesn't work when the key is held down.
        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);

        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

        // All platforms redo shortcut
        if (control && shift && key == VirtualKey.Z)
        {
            ViewModel.OnShortcutAction(ShortcutAction.Redo);
            return true;
        }

#if WINDOWS
        // Windows only undo shortcut
        if (control && key == VirtualKey.Y)
        {
            ViewModel.OnShortcutAction(ShortcutAction.Redo);
            return true;
        }
#endif

        // All platforms undo shortcut
        if (control && key == VirtualKey.Z)
        {
            ViewModel.OnShortcutAction(ShortcutAction.Undo);
            return true;
        }

        return false;
    }

    private Result OnViewModel_Navigate(Type pageType, object parameter)
    {
        if (_contentFrame.Content != null &&
            _contentFrame.Content.GetType() == pageType)
        {
            // Already at the requested page, so just early out.
            return Result.Ok();
        }

        if (_contentFrame.Navigate(pageType, parameter))
        {
            return Result.Ok();
        }
        return Result.Fail($"Failed to navigate to page type {pageType}");
    }

    private void OnMainPage_NavigationViewItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            ViewModel.SelectNavigationItem(MainPageViewModel.SettingsTag);
            return;
        }

        var item = args.InvokedItemContainer as NavigationViewItem;
        Guard.IsNotNull(item);

        var navigationItemTag = item.Tag;
        Guard.IsNotNull(navigationItemTag);

        var tag = navigationItemTag.ToString();
        Guard.IsNotNullOrEmpty(tag);

        ViewModel.SelectNavigationItem(tag);
    }

    public void Navigate(Type pageType, object parameter)
    {
        Guard.IsNotNull(_contentFrame);

        if (_contentFrame.Content is null ||
            _contentFrame.Content.GetType() != pageType)
        {
            _contentFrame.Navigate(pageType, parameter);
        }
    }
}
