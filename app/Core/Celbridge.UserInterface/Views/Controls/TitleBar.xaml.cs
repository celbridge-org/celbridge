using Celbridge.Navigation;
using Celbridge.UserInterface.ViewModels.Controls;
using Microsoft.UI.Dispatching;

namespace Celbridge.UserInterface.Views;

public sealed partial class TitleBar : UserControl, ITitleBar
{
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private Window? _mainWindow;
    private DispatcherQueueTimer? _updateRegionsTimer;

    public TitleBarViewModel ViewModel { get; }

    /// <inheritdoc/>
    public bool BuildShortcutButtons(IReadOnlyList<Shortcut> shortcuts, Action<string> onScriptExecute)
    {
        return PageNavigationToolbar.BuildShortcutButtons(shortcuts, onScriptExecute);
    }

    /// <inheritdoc/>
    public void SetShortcutButtonsVisible(bool isVisible)
    {
        PageNavigationToolbar.SetShortcutButtonsVisible(isVisible);
    }

    /// <inheritdoc/>
    public void ClearShortcutButtons()
    {
        PageNavigationToolbar.ClearShortcutButtons();
    }

    public TitleBar()
    {
        this.InitializeComponent();

        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<TitleBarViewModel>();

        this.DataContext = ViewModel;

        Loaded += OnTitleBar_Loaded;
        Unloaded += OnTitleBar_Unloaded;
    }

    private void OnTitleBar_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnLoaded();

        ApplyTooltips();

        _messengerService.Register<MainWindowActivatedMessage>(this, OnMainWindowActivated);
        _messengerService.Register<MainWindowDeactivatedMessage>(this, OnMainWindowDeactivated);

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        LayoutToolbar.SizeChanged += OnLayoutToolbar_SizeChanged;
        PageNavigationToolbar.SizeChanged += OnPageNavigationToolbar_SizeChanged;
        SettingsButton.SizeChanged += OnSettingsButton_SizeChanged;

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        _mainWindow = userInterfaceService.MainWindow as Window;

        // Listen for layout updates to recalculate interactive regions
        // This handles window maximize/restore events
        this.LayoutUpdated += OnTitleBar_LayoutUpdated;

        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateInteractiveRegions();
        });
    }

    private void OnTitleBar_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnUnloaded();

        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        LayoutToolbar.SizeChanged -= OnLayoutToolbar_SizeChanged;
        PageNavigationToolbar.SizeChanged -= OnPageNavigationToolbar_SizeChanged;
        SettingsButton.SizeChanged -= OnSettingsButton_SizeChanged;
        this.LayoutUpdated -= OnTitleBar_LayoutUpdated;

        if (_updateRegionsTimer is not null)
        {
            _updateRegionsTimer.Stop();
            _updateRegionsTimer = null;
        }

        Loaded -= OnTitleBar_Loaded;
        Unloaded -= OnTitleBar_Unloaded;

        _messengerService.UnregisterAll(this);
    }

    private void ApplyTooltips()
    {
        var settingsTooltip = _stringLocalizer.GetString("TitleBar_SettingsTooltip");
        ToolTipService.SetToolTip(SettingsButton, settingsTooltip);
        ToolTipService.SetPlacement(SettingsButton, PlacementMode.Bottom);
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.IsWorkspaceActive))
        {
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

    private void OnLayoutToolbar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0)
        {
            UpdateInteractiveRegions();
        }
    }

    private void OnPageNavigationToolbar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0)
        {
            UpdateInteractiveRegions();
        }
    }

    private void OnSettingsButton_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0)
        {
            UpdateInteractiveRegions();
        }
    }

    private void OnTitleBar_LayoutUpdated(object? sender, object e)
    {
        // LayoutUpdated fires very frequently, so throttle the updates using a timer
        // This ensures we don't recalculate interactive regions on every frame
        if (_updateRegionsTimer is null || !_updateRegionsTimer.IsRunning)
        {
            if (_updateRegionsTimer is null)
            {
                _updateRegionsTimer = DispatcherQueue.CreateTimer();
                _updateRegionsTimer.Interval = TimeSpan.FromMilliseconds(100);
                _updateRegionsTimer.Tick += (s, e) =>
                {
                    UpdateInteractiveRegions();
                    _updateRegionsTimer?.Stop();
                };
            }

            _updateRegionsTimer.Start();
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.NavigateToPage(NavigationConstants.SettingsTag);
    }

    private void UpdateInteractiveRegions()
    {
#if WINDOWS
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

            var regions = new List<Windows.Graphics.RectInt32>();

            if (PageNavigationToolbar.ActualWidth > 0)
            {
                var navTransform = PageNavigationToolbar.TransformToVisual(_mainWindow.Content);
                var navPosition = navTransform.TransformPoint(new Windows.Foundation.Point(0, 0));

                regions.Add(new Windows.Graphics.RectInt32(
                    (int)(navPosition.X * scale),
                    (int)(navPosition.Y * scale),
                    (int)(PageNavigationToolbar.ActualWidth * scale),
                    (int)(PageNavigationToolbar.ActualHeight * scale)
                ));
            }

            if (ViewModel.IsWorkspaceActive && LayoutToolbar.ActualWidth > 0)
            {
                var toolbarTransform = LayoutToolbar.TransformToVisual(_mainWindow.Content);
                var toolbarPosition = toolbarTransform.TransformPoint(new Windows.Foundation.Point(0, 0));

                regions.Add(new Windows.Graphics.RectInt32(
                    (int)(toolbarPosition.X * scale),
                    (int)(toolbarPosition.Y * scale),
                    (int)(LayoutToolbar.ActualWidth * scale),
                    (int)(LayoutToolbar.ActualHeight * scale)
                ));
            }

            if (SettingsButton.ActualWidth > 0)
            {
                var settingsTransform = SettingsButton.TransformToVisual(_mainWindow.Content);
                var settingsPosition = settingsTransform.TransformPoint(new Windows.Foundation.Point(0, 0));

                regions.Add(new Windows.Graphics.RectInt32(
                    (int)(settingsPosition.X * scale),
                    (int)(settingsPosition.Y * scale),
                    (int)(SettingsButton.ActualWidth * scale),
                    (int)(SettingsButton.ActualHeight * scale)
                ));
            }

            if (regions.Count > 0)
            {
                nonClientInputSrc.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.Passthrough, regions.ToArray());
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
