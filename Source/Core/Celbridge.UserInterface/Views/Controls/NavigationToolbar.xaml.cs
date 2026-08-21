using Celbridge.Platform;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Views;

public sealed partial class NavigationToolbar : UserControl
{
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private MainMenu? _mainMenu;

    public NavigationToolbar()
    {
        this.InitializeComponent();

        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();

        // The menu opens over the document area, where a hosted web view would take the click too.
        var overlayInputSuppressor = ServiceLocator.AcquireService<IOverlayInputSuppressor>();
        overlayInputSuppressor.SuppressWhileOpen(MainMenuFlyout);

        Loaded += OnNavigationToolbar_Loaded;
        Unloaded += OnNavigationToolbar_Unloaded;
    }

    private void OnNavigationToolbar_Loaded(object sender, RoutedEventArgs e)
    {
        // macOS surfaces these commands through the native menubar, so the in-window hamburger menu is
        // shown only on platforms without one (Windows, Linux).
        var platformInfo = ServiceLocator.AcquireService<IPlatformInfo>();
        if (!platformInfo.UsesNativeMenuBar)
        {
            _mainMenu = new MainMenu(MainMenuFlyout);
            _mainMenu.OnLoaded();
            MainMenuButton.Visibility = Visibility.Visible;
        }

        ApplyTooltips();
        UpdateWordmarkVisibility();

        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);
        _messengerService.Register<WorkspaceUnloadedMessage>(this, OnWorkspaceUnloaded);
    }

    private void OnNavigationToolbar_Unloaded(object sender, RoutedEventArgs e)
    {
        _mainMenu?.OnUnloaded();

        Loaded -= OnNavigationToolbar_Loaded;
        Unloaded -= OnNavigationToolbar_Unloaded;

        _messengerService.UnregisterAll(this);
    }

    /// <summary>
    /// The controls in this toolbar that a user can click.
    /// </summary>
    internal IReadOnlyList<FrameworkElement> GetInteractiveElements()
    {
        var elements = new List<FrameworkElement>
        {
            MainMenuButton,
            ProjectSwitcher,
            ProjectHealthButton
        };

        return elements;
    }

    private void ApplyTooltips()
    {
        var mainMenuTooltip = _stringLocalizer.GetString("TitleBar_MainMenuTooltip");
        ToolTipService.SetToolTip(MainMenuButton, mainMenuTooltip);
        ToolTipService.SetPlacement(MainMenuButton, PlacementMode.Bottom);
        AutomationProperties.SetName(MainMenuButton, mainMenuTooltip);
    }

    private void OnWorkspaceLoaded(object recipient, WorkspaceLoadedMessage message)
    {
        UpdateWordmarkVisibility();
    }

    private void OnWorkspaceUnloaded(object recipient, WorkspaceUnloadedMessage message)
    {
        UpdateWordmarkVisibility();
    }

    private void UpdateWordmarkVisibility()
    {
        // The switcher occupies this slot while a project is loaded, and collapses itself when none is.
        Wordmark.Visibility = _workspaceWrapper.IsWorkspacePageLoaded
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
