using Celbridge.Platform;
using Celbridge.UserInterface.Services;

namespace Celbridge.UserInterface.Views;

public sealed partial class ProjectToolbar : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;
    private MainMenu? _mainMenu;

    public ProjectToolbar()
    {
        this.InitializeComponent();

        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        // The menu opens over the document area, where a hosted web view would take the click too.
        var overlayInputSuppressor = ServiceLocator.AcquireService<IOverlayInputSuppressor>();
        overlayInputSuppressor.SuppressWhileOpen(MainMenuFlyout);

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

        Loaded -= OnProjectToolbar_Loaded;
        Unloaded -= OnProjectToolbar_Unloaded;
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
}
