using Celbridge.UserInterface.ViewModels.Controls;

namespace Celbridge.UserInterface.Views;

public sealed partial class TitleBar : UserControl
{
    private readonly IMessengerService _messengerService;
    private Window? _mainWindow;

    public TitleBarViewModel ViewModel { get; }

    public TitleBar()
    {
        this.InitializeComponent();

        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        ViewModel = ServiceLocator.AcquireService<TitleBarViewModel>();

        this.DataContext = ViewModel;

        Loaded += OnTitleBar_Loaded;
        Unloaded += OnTitleBar_Unloaded;
    }

    private void OnTitleBar_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnLoaded();

        // Register for workspace activation messages to handle visual states
        _messengerService.Register<MainWindowActivatedMessage>(this, OnMainWindowActivated);
        _messengerService.Register<MainWindowDeactivatedMessage>(this, OnMainWindowDeactivated);

        // Listen to ViewModel property changes to update interactive regions
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Update interactive regions when toolbar size changes
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
        ViewModel.OnUnloaded();

        // Unregister all event handlers to avoid memory leaks
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        PanelToolbar.SizeChanged -= OnPanelToolbar_SizeChanged;

        Loaded -= OnTitleBar_Loaded;
        Unloaded -= OnTitleBar_Unloaded;

        _messengerService.UnregisterAll(this);
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.IsWorkspaceActive))
        {
            // Update interactive regions when workspace activation state changes
            UpdateInteractiveRegions();
        }
    }

    private void OnMainWindowActivated(object recipient, MainWindowActivatedMessage message)
    {
        VisualStateManager.GoToState(this, "Active", false);
    }

    private void OnMainWindowDeactivated(object recipient, MainWindowDeactivatedMessage message)
    {
        VisualStateManager.GoToState(this, "Inactive", false);
    }

    private void OnPanelToolbar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Update interactive regions whenever the toolbar size changes
        if (ViewModel.IsWorkspaceActive && e.NewSize.Width > 0)
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

            // Add passthrough region for the panel toolbar if workspace is active
            if (ViewModel.IsWorkspaceActive && PanelToolbar.ActualWidth > 0)
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
    /// to update the interactive regions for the title bar. This prevents double 
    /// clicks on the panel toggles registering as double clicks on the title bar.
    /// </summary>
    public void RefreshInteractiveRegions()
    {
        UpdateInteractiveRegions();
    }
}
