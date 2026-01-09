using Celbridge.Workspace;

namespace Celbridge.UserInterface.Views;

public sealed partial class TitleBar : UserControl
{
    private readonly IMessengerService _messengerService;
    private IStringLocalizer _stringLocalizer;
    private Window? _mainWindow;

    private string ApplicationNameString => _stringLocalizer.GetString("ApplicationName");

    public TitleBar()
    {
        InitializeComponent();

        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        Loaded += OnTitleBar_Loaded;
        Unloaded += OnTitleBar_Unloaded;
    }

    private void OnTitleBar_Loaded(object sender, RoutedEventArgs e)
    {
        _messengerService.Register<MainWindowActivatedMessage>(this, OnMainWindowActivated);
        _messengerService.Register<MainWindowDeactivatedMessage>(this, OnMainWindowDeactivated);
        _messengerService.Register<WorkspacePageActivatedMessage>(this, OnWorkspacePageActivated);
        _messengerService.Register<WorkspacePageDeactivatedMessage>(this, OnWorkspacePageDeactivated);

        // Update interactive regions when sizes change
        PanelToolbar.SizeChanged += OnPanelToolbar_SizeChanged;

        // Cache the main window reference
        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        _mainWindow = userInterfaceService.MainWindow as Window;


        // Initial update of interactive regions after layout
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateInteractiveRegions();
        });
    }

    private void OnTitleBar_Unloaded(object sender, RoutedEventArgs e)
    {
        // Unregister all event handlers to avoid memory leaks
        PanelToolbar.SizeChanged -= OnPanelToolbar_SizeChanged;

        Loaded -= OnTitleBar_Loaded;
        Unloaded -= OnTitleBar_Unloaded;

        _messengerService.UnregisterAll(this);
    }

    private void OnMainWindowActivated(object recipient, MainWindowActivatedMessage message)
    {
        VisualStateManager.GoToState(this, "Active", false);
    }

    private void OnMainWindowDeactivated(object recipient, MainWindowDeactivatedMessage message)
    {
        VisualStateManager.GoToState(this, "Inactive", false);
    }

    private void OnWorkspacePageActivated(object recipient, WorkspacePageActivatedMessage message)
    {
        // Show the panel toggle toolbar when navigating to the workspace page
        PanelToolbar.Visibility = Visibility.Visible;
        
        // The toolbar needs to be laid out before we can calculate its position
        // The SizeChanged event will trigger UpdateInteractiveRegions after layout
    }

    private void OnWorkspacePageDeactivated(object recipient, WorkspacePageDeactivatedMessage message)
    {
        // Hide the panel toggle toolbar when navigating away from the workspace page
        PanelToolbar.Visibility = Visibility.Collapsed;
        UpdateInteractiveRegions();
    }

    private void OnPanelToolbar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Update interactive regions whenever the toolbar size changes
        if (PanelToolbar.Visibility == Visibility.Visible && e.NewSize.Width > 0)
        {
            UpdateInteractiveRegions();
        }
    }

    private void UpdateInteractiveRegions()
    {
#if WINDOWS
        // For Windows, we need to set the input non-client pointer source to allow
        // interactivity with the panel toggle toolbar in the title bar area.
        try
        {
            if (_mainWindow == null)
            {
                return;
            }

            var appWindow = _mainWindow.AppWindow;
            if (appWindow == null)
            {
                return;
            }

            var nonClientInputSrc = Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(appWindow.Id);
            var scale = _mainWindow.Content.XamlRoot?.RasterizationScale ?? 1.0;

            // Add passthrough region for the panel toolbar if visible
            if (PanelToolbar.Visibility == Visibility.Visible && PanelToolbar.ActualWidth > 0)
            {
                var toolbarTransform = PanelToolbar.TransformToVisual(_mainWindow.Content);
                var toolbarPosition = toolbarTransform.TransformPoint(new Windows.Foundation.Point(0, 0));
                
                var rect = new Windows.Graphics.RectInt32(
                    (int)(toolbarPosition.X * scale),
                    (int)(toolbarPosition.Y * scale),
                    (int)(PanelToolbar.ActualWidth * scale),
                    (int)(PanelToolbar.ActualHeight * scale)
                );

                nonClientInputSrc.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.Passthrough, [rect]);
            }
            else
            {
                nonClientInputSrc.ClearRegionRects(Microsoft.UI.Input.NonClientRegionKind.Passthrough);
            }
        }
        catch
        {
            // Silently ignore any errors
        }
#endif
    }

    public void SetProjectTitle(string title)
    {
        ProjectNameText.Text = title;
    }

    /// <summary>
    /// Call this method after the toolbar becomes visible and has been laid out
    /// to update the interactive regions for the title bar.
    /// This prevents double clicks on the panel toggles registering as double clicks on
    /// the title bar.
    /// </summary>
    public void RefreshInteractiveRegions()
    {
        UpdateInteractiveRegions();
    }
}

