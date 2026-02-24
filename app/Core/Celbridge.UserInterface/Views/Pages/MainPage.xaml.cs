using Celbridge.Logging;
using Celbridge.Navigation;
using Celbridge.UserInterface.ViewModels.Pages;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace Celbridge.UserInterface.Views;

public partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; private set; }

    private IUserInterfaceService _userInterfaceService;
    private IMessengerService _messengerService;
    private INavigationService _navigationService;
    private readonly ILogger<MainPage> _logger;

    private Grid _layoutRoot;
    private Grid _contentArea;
    private readonly Dictionary<Type, Page> _pageCache = new();
    private Page? _currentPage;

#if WINDOWS
    private TitleBar? _titleBar;
#endif

    public MainPage()
    {
        InitializeComponent();

        _userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _navigationService = ServiceLocator.AcquireService<INavigationService>();
        _logger = ServiceLocator.AcquireService<ILogger<MainPage>>();

        ViewModel = ServiceLocator.AcquireService<MainPageViewModel>();

        _contentArea = new Grid()
            .Background(ThemeResource.Get<Brush>("ApplicationBackgroundBrush"))
            .Name("ContentArea");

        _layoutRoot = new Grid()
            .Name("LayoutRoot")
            .RowDefinitions("Auto, *")
            .Children(_contentArea);

        // Position the content area in the second row (below the title bar)
        Grid.SetRow(_contentArea, 1);

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
        // Setup the custom title bar (Windows only)
        _titleBar = new TitleBar();
        _layoutRoot.Children.Add(_titleBar);

        mainWindow.ExtendsContentIntoTitleBar = true;
        mainWindow.SetTitleBar(_titleBar);

        // Configure the AppWindow titlebar to use taller caption buttons (48px instead of 32px)
        // This makes the system minimize/maximize/close buttons larger to match the increased titlebar height
        var appWindow = mainWindow.AppWindow;
        if (appWindow?.TitleBar != null)
        {
            appWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall;
        }

        _userInterfaceService.RegisterTitleBar(_titleBar);
#endif

        // Register for window mode changes
        _messengerService.Register<WindowModeChangedMessage>(this, OnWindowLayoutChanged);

        // Register the navigation handler
        var navigationService = _navigationService as Celbridge.UserInterface.Services.NavigationService;
        Guard.IsNotNull(navigationService);
        navigationService.SetNavigateHandler(NavigateToPage);

        ViewModel.OnMainPage_Loaded();

        // Listen for keyboard input events (required for undo / redo)
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
        if (mainWindow.CoreWindow is not null)
        {
            mainWindow.CoreWindow.KeyDown += (s, e) =>
            {
                if (OnKeyDown(e.VirtualKey))
                {
                    e.Handled = true;
                }
            };
        }
#endif
    }

    private void OnMainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnMainPage_Unloaded();

        // Unregister all event handlers to avoid memory leaks
        _messengerService.UnregisterAll(this);

        Loaded -= OnMainPage_Loaded;
        Unloaded -= OnMainPage_Unloaded;
    }

    private void OnWindowLayoutChanged(object recipient, WindowModeChangedMessage message)
    {
#if WINDOWS
        // Show/hide the title bar based on window mode
        // In Windowed, FullScreen, and ZenMode modes, the title bar is visible
        // In Presenter mode, the title bar is hidden
        if (_titleBar != null)
        {
            bool showTitleBar = message.WindowMode == WindowMode.Windowed || 
                                message.WindowMode == WindowMode.FullScreen ||
                                message.WindowMode == WindowMode.ZenMode;
            _titleBar.Visibility = showTitleBar ? Visibility.Visible : Visibility.Collapsed;
        }
#endif
    }

    private bool OnKeyDown(VirtualKey key)
    {
        // Use the HasFlag method to check if the control key is down.
        // If you just compare with CoreVirtualKeyStates.Down it doesn't work when the key is held down.
        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);

        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

        var alt = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu)
            .HasFlag(CoreVirtualKeyStates.Down);

        var shortcutService = ServiceLocator.AcquireService<IKeyboardShortcutService>();
        return shortcutService.HandleGlobalShortcut(key, control, shift, alt);
    }

    public Result NavigateToPage(Type pageType, object? parameter = null)
    {
        if (_currentPage?.GetType() == pageType)
        {
            // Already at the requested page, so just early out.
            return Result.Ok();
        }

        // If workspace cleanup was requested, mark the current page for removal
        if (_currentPage != null && _navigationService.IsWorkspacePageCleanupPending)
        {
            _currentPage.NavigationCacheMode = NavigationCacheMode.Disabled;
        }

        // Handle current page teardown
        if (_currentPage != null)
        {
            if (_currentPage.NavigationCacheMode == NavigationCacheMode.Disabled)
            {
                // Page was marked for cleanup, remove from cache and visual tree
                _pageCache.Remove(_currentPage.GetType());
                _contentArea.Children.Remove(_currentPage);
            }
            else if (_pageCache.ContainsKey(_currentPage.GetType()))
            {
                // Cached page: hide but keep in visual tree so it receives theme updates
                _currentPage.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Non-cached page: remove from visual tree
                _contentArea.Children.Remove(_currentPage);
            }
        }

        // Get or create target page
        if (!_pageCache.TryGetValue(pageType, out var page))
        {
            try
            {
                page = (Page)Activator.CreateInstance(pageType)!;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create page of type {PageType}", pageType);
                return Result.Fail($"Failed to create page of type {pageType}");
            }

            // Cache pages that request caching
            if (page.NavigationCacheMode == NavigationCacheMode.Required ||
                page.NavigationCacheMode == NavigationCacheMode.Enabled)
            {
                _pageCache[pageType] = page;
            }
        }

        // Add to visual tree if not already there
        if (!_contentArea.Children.Contains(page))
        {
            _contentArea.Children.Add(page);
        }

        // Pass the navigation parameter via Tag so the page can read it in its Loaded handler
        page.Tag = parameter;

        page.Visibility = Visibility.Visible;
        _currentPage = page;
        return Result.Ok();
    }
}
