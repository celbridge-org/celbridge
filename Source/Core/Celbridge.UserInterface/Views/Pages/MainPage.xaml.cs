using Celbridge.Logging;
using Celbridge.Navigation;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.ViewModels.Pages;
using Windows.System;

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
    private FrameworkElement? _titleBar;

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

    private async void OnMainPage_Loaded(object sender, RoutedEventArgs e)
    {
        var mainWindow = _userInterfaceService.MainWindow as Window;
        Guard.IsNotNull(mainWindow);

        // The application toolbar (page navigation, layout toggles, settings) occupies row 0 of the layout
        // grid. Each platform hosts it differently: inside the custom title bar on the packaged Windows
        // head, or directly beneath the native title bar on the Skia desktop heads.
        var applicationToolbarHost = ServiceLocator.AcquireService<IApplicationToolbarHost>();
        var titleBar = applicationToolbarHost.Install(mainWindow, _layoutRoot);
        _titleBar = (FrameworkElement)titleBar;
        _userInterfaceService.RegisterTitleBar(titleBar);

        // Keep the AppKit first responder aligned with managed-panel focus so the native Edit-menu
        // shortcuts fall through to Uno's keyboard handling. macOS-only. A no-op elsewhere.
        Celbridge.UserInterface.Platform.MacOSManagedPanelResponder.Start(_messengerService);

        // Register for layout mode changes
        _messengerService.Register<LayoutModeChangedMessage>(this, OnLayoutModeChanged);

        // Register the navigation handler
        var navigationService = _navigationService as Celbridge.UserInterface.Services.NavigationService;
        Guard.IsNotNull(navigationService);
        navigationService.SetNavigateHandler(NavigateToPage);

        await ViewModel.OnMainPage_LoadedAsync();

        // Listen for keyboard input events (required for undo / redo and other app shortcuts).
        // Window.CoreWindow is a legacy UWP API that is null on the Skia desktop head, so the root
        // content's KeyDown is used on every head.
        var rootContent = mainWindow.Content;
        Guard.IsNotNull(rootContent);

        // Register with handledEventsToo so app shortcuts (undo / redo) are received even when the
        // focused control (the Explorer tree, Inspector, or toolbar) marks the key event handled before
        // it bubbles to the root. A plain KeyDown += handler is skipped for already-handled events.
        rootContent.AddHandler(
            UIElement.KeyDownEvent,
            new Microsoft.UI.Xaml.Input.KeyEventHandler(OnRootContentKeyDown),
            handledEventsToo: true);
    }

    private void OnMainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnMainPage_Unloaded();

        // Unregister all event handlers to avoid memory leaks
        _messengerService.UnregisterAll(this);

        Loaded -= OnMainPage_Loaded;
        Unloaded -= OnMainPage_Unloaded;
    }

    private void OnLayoutModeChanged(object recipient, LayoutModeChangedMessage message)
    {
        // Show/hide the application toolbar based on the layout mode. Default and Focus keep the
        // toolbar; Presentation hides it so only the document content is shown.
        if (_titleBar != null)
        {
            bool showToolbar = message.LayoutMode != LayoutMode.Presentation;
            _titleBar.Visibility = showToolbar ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnRootContentKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (OnKeyDown(e.Key))
        {
            e.Handled = true;
        }
    }

    private bool OnKeyDown(VirtualKey key)
    {
        // The command modifier folds in Cmd on macOS (which the head reports as the left Windows key),
        // so Cmd+Z / Cmd+Shift+Z drive undo/redo there.
        var control = EditKeyboard.IsCommandModifierDown();
        var shift = EditKeyboard.IsShiftDown();
        var alt = EditKeyboard.IsAltDown();

        var shortcutService = ServiceLocator.AcquireService<IKeyboardShortcutService>();
        return shortcutService.HandleShortcut(key, control, shift, alt);
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
