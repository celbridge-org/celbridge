using Celbridge.Logging;
using Celbridge.UserInterface.Platform;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.ViewModels.Pages;
using Celbridge.UserInterface.Views.Controls;
using Celbridge.WebHost;
using Celbridge.Workspace;
using Windows.System;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// The window's root view. Which base view fills its content area is the application shell's business.
/// </summary>
public class MainPage : Page
{
    public MainPageViewModel ViewModel { get; private set; }

    private IUserInterfaceService _userInterfaceService;
    private IMessengerService _messengerService;
    private readonly ILogger<MainPage> _logger;

    private Grid _layoutRoot;
    private Grid _contentArea;
    private FrameworkElement? _titleBar;

    public MainPage()
    {
        _userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _logger = ServiceLocator.AcquireService<ILogger<MainPage>>();

        ViewModel = ServiceLocator.AcquireService<MainPageViewModel>();

        _contentArea = new Grid()
            .Background(ThemeResource.Get<Brush>("ChromeBackgroundBrush"))
            .Name("ContentArea");

        // Host the spotlight callout in the app shell so it can point at any landmark, including
        // the title-bar chrome above the pages. It registers itself with the spotlight service and
        // lives for the app lifetime.
        var spotlightView = new SpotlightView();

        _layoutRoot = new Grid()
            .Name("LayoutRoot")
            .RowDefinitions("Auto, *")
            .Children(_contentArea, spotlightView);

        // Position the content area in the second row (below the title bar)
        Grid.SetRow(_contentArea, 1);
        Grid.SetRowSpan(spotlightView, 2);

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
        _titleBar = applicationToolbarHost.Install(mainWindow, _layoutRoot);

        // Keep the AppKit first responder aligned with managed-panel focus so the native Edit-menu
        // shortcuts fall through to Uno's keyboard handling. macOS-only. A no-op elsewhere.
        var focusReconciler = ServiceLocator.AcquireService<IFocusReconciler>();
        MacOSManagedPanelResponder.Start(_messengerService, focusReconciler);

        // Deliver document keys the focused WKWebView would otherwise swallow: Tab to the focused web
        // surface (editor indent, or the page's own form-field navigation) instead of letting the managed
        // focus loop move focus out of it, and Command+W / Command+Shift+W to the close-document shortcuts.
        // macOS-only. A no-op elsewhere.
        var focusServiceForKeyMonitor = ServiceLocator.AcquireService<IFocusService>();
        var webViewFocusRegistry = ServiceLocator.AcquireService<IWebViewFocusRegistry>();
        MacOSKeyEventMonitor.Start(focusServiceForKeyMonitor, webViewFocusRegistry, _messengerService, _logger);

        // Undo native first-responder resigns caused by managed-focus housekeeping, which would otherwise
        // deactivate the focused web surface (hidden caret, beeping keys). macOS-only. A no-op elsewhere.
        MacOSFirstResponderMonitor.Start(focusReconciler, _logger);

        // Let overlays take the clicks that land over a hosted web view, which would otherwise act on them
        // as well as the overlay. macOS-only. A no-op elsewhere.
        MacOSWebViewInputSuppressor.Start(_logger);

        // Route the editing keys Uno diverts away from the native first responder (Backspace, Enter,
        // arrows) into the focused web surface instead of dropping them. macOS-only. A no-op elsewhere.
        MacOSKeyCommandRouter.SetFocusRegistry(webViewFocusRegistry);

        // Register for layout mode changes
        _messengerService.Register<LayoutModeChangedMessage>(this, OnLayoutModeChanged);

        // Hand the shell the area it shows the base view in. It shows Home until a project loads.
        // Setting it up is not part of IApplicationShell, which is about which base view is showing rather
        // than where it is shown.
        var applicationShell = ServiceLocator.AcquireService<IApplicationShell>() as ApplicationShell;
        Guard.IsNotNull(applicationShell);
        applicationShell.SetContentArea(_contentArea);

        ViewModel.OnMainPage_Loaded();

        // Listen for keyboard input events (required for undo / redo and other app shortcuts).
        // Window.CoreWindow is a legacy UWP API that is null on the Skia desktop head, so the root
        // content's KeyDown is used on every head.
        var rootContent = mainWindow.Content;
        Guard.IsNotNull(rootContent);

        // Register with handledEventsToo so app shortcuts (undo / redo) are received even when the
        // focused control (the Explorer tree, a document, or a toolbar) marks the key event handled before
        // it bubbles to the root. A plain KeyDown += handler is skipped for already-handled events.
        rootContent.AddHandler(
            UIElement.KeyDownEvent,
            new Microsoft.UI.Xaml.Input.KeyEventHandler(OnRootContentKeyDown),
            handledEventsToo: true);

        // Any press ends a grant's hold on the focused panel: what follows is the user's new intent rather
        // than the tail of the gesture the grant was defending against. Registered on the root with
        // handledEventsToo so a control that handles the press cannot hide it.
        rootContent.AddHandler(
            UIElement.PointerPressedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(OnRootContentPointerPressed),
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
        // toolbar. Presentation hides it so only the document content is shown.
        if (_titleBar != null)
        {
            bool showToolbar = message.LayoutMode != LayoutMode.Presentation;
            _titleBar.Visibility = showToolbar ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnRootContentPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        FocusIntent.EndPanelClaimSuppression();
    }

    private void OnRootContentKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        FocusIntent.EndPanelClaimSuppression();

        // Uno dispatches Backspace and Enter as managed KeyDown events while a web surface holds focus,
        // unlike the other editing keys, so the router delivers them to the surface from here. A key a
        // managed text control has handled or holds keyboard focus for (the address box, the find bar) is
        // left to that control: forwarding it too would deliver the key twice. See MacOSKeyCommandRouter.
        if (!e.Handled &&
            !IsTextBoxFocused() &&
            MacOSKeyCommandRouter.TryForwardManagedEditingKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (OnKeyDown(e.Key))
        {
            e.Handled = true;
        }
    }

    private bool IsTextBoxFocused()
    {
        var xamlRoot = XamlRoot;
        if (xamlRoot is null)
        {
            return false;
        }

        return Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot) is TextBox;
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
}
