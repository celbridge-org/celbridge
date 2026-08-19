using Celbridge.Messaging;
using Celbridge.Navigation;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using Celbridge.Platform;
using Celbridge.UserInterface.ViewModels.Controls;
using Microsoft.UI.Dispatching;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// The application toolbar. Platform-neutral content that each head hosts in its own title-bar
/// arrangement.
/// </summary>
public sealed partial class ApplicationToolbar : UserControl
{
    /// <summary>
    /// The height of the toolbar strip. It occupies the row above the page content on every head, so the
    /// window minimum is composed from it as well.
    /// </summary>
    public const double ToolbarHeight = 40;

    private readonly IStringLocalizer _stringLocalizer;
    private readonly IMessengerService _messengerService;
    private DispatcherQueueTimer? _layoutChangedTimer;

    /// <summary>
    /// Raised when the interactive toolbar elements may have moved or resized, so a host can refresh
    /// any window-chrome regions derived from them. Throttled to avoid per-frame churn, and not raised
    /// when nothing is listening.
    /// </summary>
    public event EventHandler? InteractiveLayoutChanged;

    public TitleBarViewModel ViewModel { get; }

    public ApplicationToolbar()
    {
        this.InitializeComponent();

        ToolbarRow.Height = new Microsoft.UI.Xaml.GridLength(ToolbarHeight);

        // The app icon column spans the utility rail below it, keeping the mark over that column's buttons.
        AppIconColumn.Width = new Microsoft.UI.Xaml.GridLength(WorkspaceConstants.UtilityPanelRailWidth);

        var platformInfo = ServiceLocator.AcquireService<IPlatformInfo>();
        if (platformInfo.ReservesWindowCaptionButtons)
        {
            // Reserve space at the right of the toolbar grid for the system caption buttons so the title-bar
            // background extends behind them. The caption buttons are drawn over this column by the platform.
            CaptionButtonsColumn.Width = new Microsoft.UI.Xaml.GridLength(144);
        }

        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        ViewModel = ServiceLocator.AcquireService<TitleBarViewModel>();

        this.DataContext = ViewModel;

        Loaded += OnApplicationToolbar_Loaded;
        Unloaded += OnApplicationToolbar_Unloaded;
    }

    private void OnApplicationToolbar_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnLoaded();

        ApplyTooltips();

        _messengerService.Register<ActivePageChangedMessage>(this, OnActivePageChanged);

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        LayoutToolbar.SizeChanged += OnInteractiveElement_SizeChanged;
        NavigationToolbar.SizeChanged += OnInteractiveElement_SizeChanged;
        SettingsButton.SizeChanged += OnInteractiveElement_SizeChanged;

        // A host that derives window-chrome regions from the toolbar (the Windows TitleBar wrapper)
        // recomputes them when the layout shifts, e.g. on window maximize/restore.
        this.LayoutUpdated += OnApplicationToolbar_LayoutUpdated;

        DispatcherQueue.TryEnqueue(RaiseInteractiveLayoutChanged);
    }

    private void OnApplicationToolbar_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnUnloaded();

        _messengerService.UnregisterAll(this);

        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        LayoutToolbar.SizeChanged -= OnInteractiveElement_SizeChanged;
        NavigationToolbar.SizeChanged -= OnInteractiveElement_SizeChanged;
        SettingsButton.SizeChanged -= OnInteractiveElement_SizeChanged;
        this.LayoutUpdated -= OnApplicationToolbar_LayoutUpdated;

        if (_layoutChangedTimer is not null)
        {
            _layoutChangedTimer.Stop();
            _layoutChangedTimer = null;
        }

        Loaded -= OnApplicationToolbar_Loaded;
        Unloaded -= OnApplicationToolbar_Unloaded;
    }

    /// <summary>
    /// The toolbar elements that should pass pointer input through to the application rather than the
    /// window-drag chrome.
    /// </summary>
    internal IReadOnlyList<FrameworkElement> GetPassthroughElements()
    {
        var candidates = new List<FrameworkElement>();

        candidates.AddRange(NavigationToolbar.GetInteractiveElements());
        candidates.AddRange(LayoutToolbar.GetInteractiveElements());
        candidates.Add(SettingsButton);

        var elements = new List<FrameworkElement>();
        foreach (var candidate in candidates)
        {
            // A collapsed control, or one that has not been laid out yet, has no region to carve out.
            if (candidate.ActualWidth > 0
                && candidate.ActualHeight > 0)
            {
                elements.Add(candidate);
            }
        }

        return elements;
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
            RaiseInteractiveLayoutChanged();
        }
    }

    private void OnInteractiveElement_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0)
        {
            RaiseInteractiveLayoutChanged();
        }
    }

    private void OnApplicationToolbar_LayoutUpdated(object? sender, object e)
    {
        // LayoutUpdated fires very frequently, so throttle the notification with a timer rather than
        // raising it on every frame. Skipped entirely when no host is listening (e.g. on the Skia
        // desktop heads, where there is no window-chrome region to maintain).
        if (InteractiveLayoutChanged is null)
        {
            return;
        }

        if (_layoutChangedTimer is null)
        {
            _layoutChangedTimer = DispatcherQueue.CreateTimer();
            _layoutChangedTimer.Interval = TimeSpan.FromMilliseconds(100);
            _layoutChangedTimer.Tick += (s, args) =>
            {
                _layoutChangedTimer?.Stop();
                RaiseInteractiveLayoutChanged();
            };
        }

        if (!_layoutChangedTimer.IsRunning)
        {
            _layoutChangedTimer.Start();
        }
    }

    private void OnActivePageChanged(object recipient, ActivePageChangedMessage message)
    {
        SettingsButton.IsChecked = message.ActivePage == ApplicationPage.Settings;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.NavigateToPage(NavigationConstants.SettingsTag);
    }

    private void RaiseInteractiveLayoutChanged()
    {
        InteractiveLayoutChanged?.Invoke(this, EventArgs.Empty);
    }
}
