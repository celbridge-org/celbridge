using Celbridge.Platform;
using Celbridge.UserInterface.Services;

namespace Celbridge.UserInterface.Views;

public sealed partial class ProjectToolbar : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;
    private MainMenu? _mainMenu;

    /// <summary>
    /// Raised when a control in this toolbar appears, disappears or changes width without the toolbar itself
    /// necessarily being resized.
    /// </summary>
    internal event EventHandler? InteractiveLayoutChanged;

    public ProjectToolbar()
    {
        this.InitializeComponent();

        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        var overlayFlyoutSupport = ServiceLocator.AcquireService<IOverlayFlyoutSupport>();
        overlayFlyoutSupport.Apply(MainMenuFlyout);

        NotificationBadge.LayoutChanged += OnBadge_LayoutChanged;
        DownloadBadge.LayoutChanged += OnBadge_LayoutChanged;

        Loaded += OnProjectToolbar_Loaded;
        Unloaded += OnProjectToolbar_Unloaded;
    }

    private void OnProjectToolbar_Loaded(object sender, RoutedEventArgs e)
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
    }

    private void OnProjectToolbar_Unloaded(object sender, RoutedEventArgs e)
    {
        _mainMenu?.OnUnloaded();

        NotificationBadge.LayoutChanged -= OnBadge_LayoutChanged;
        DownloadBadge.LayoutChanged -= OnBadge_LayoutChanged;

        Loaded -= OnProjectToolbar_Loaded;
        Unloaded -= OnProjectToolbar_Unloaded;
    }

    private void OnBadge_LayoutChanged(object? sender, EventArgs e)
    {
        InteractiveLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The controls in this toolbar that a user can click.
    /// </summary>
    internal IReadOnlyList<FrameworkElement> GetInteractiveElements()
    {
        var elements = new List<FrameworkElement>
        {
            MainMenuButton,
            ProjectSwitcher
        };

        // A collapsed control keeps the size and position it was last measured at, so a badge's visibility
        // is what says whether it is on screen.
        if (NotificationBadge.Visibility == Visibility.Visible)
        {
            elements.Add(NotificationBadge);
        }

        if (DownloadBadge.Visibility == Visibility.Visible)
        {
            elements.Add(DownloadBadge);
        }

        return elements;
    }

    private void ApplyTooltips()
    {
        var mainMenuTooltip = _stringLocalizer.GetString("TitleBar_MainMenuTooltip");
        ToolTipService.SetToolTip(MainMenuButton, mainMenuTooltip);
        ToolTipService.SetPlacement(MainMenuButton, PlacementMode.Bottom);
        AutomationProperties.SetName(MainMenuButton, mainMenuTooltip);
    }
}
