using Celbridge.Browser.ViewModels;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Dispatching;

namespace Celbridge.Browser.Views;

/// <summary>
/// The row at the top of a browser document: the tabs, and the button that opens a blank tab.
/// </summary>
public sealed partial class BrowserTabBar : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly BrowserTabDragHandler _dragHandler;

    private BrowserDocumentViewModel? _viewModel;

    /// <summary>
    /// Raised when the user closes a tab with its close button.
    /// </summary>
    public event EventHandler<BrowserTabViewModel>? TabCloseRequested;

    public BrowserTabBar()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        InitializeComponent();

        _dragHandler = new BrowserTabDragHandler(TabItems, DragOverlay, InsertionMarker);

        string newTabTooltip = _stringLocalizer.GetString("Browser_TabBar_NewTabTooltip");
        ToolTipService.SetToolTip(NewTabButton, newTabTooltip);
        AutomationProperties.SetName(NewTabButton, newTabTooltip);
    }

    /// <summary>
    /// The document's view model. Set once, when the document view is constructed.
    /// </summary>
    public BrowserDocumentViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            _dragHandler.ViewModel = value;
            TabItems.ItemsSource = value?.Tabs;
        }
    }

    // Limits the tabs to the width left beside the new tab button. The button sits right after the last tab, and stays
    // in the row with many tabs open.
    private void BarRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var gapsWidth = (BarRoot.ColumnDefinitions.Count - 1) * BarRoot.ColumnSpacing;

        TabScrollViewer.MaxWidth = Math.Max(0, e.NewSize.Width - NewTabButton.ActualWidth - gapsWidth);
    }

    /// <summary>
    /// Scrolls the shown tab into view once the tab strip has been laid out.
    /// </summary>
    public void BringShownTabIntoView()
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ScrollToShownTab);
    }

    private void ScrollToShownTab()
    {
        var shownTab = ViewModel?.ShownTab;
        if (shownTab is null
            || TabItems.ContainerFromItem(shownTab) is not FrameworkElement container
            || container.ActualWidth <= 0)
        {
            return;
        }

        var origin = container.TransformToVisual(TabItems).TransformPoint(default);
        var offset = TabScrollViewer.HorizontalOffset;
        var viewportWidth = TabScrollViewer.ViewportWidth;

        if (origin.X < offset)
        {
            TabScrollViewer.ChangeView(origin.X, null, null);
        }
        else if (origin.X + container.ActualWidth > offset + viewportWidth)
        {
            TabScrollViewer.ChangeView(origin.X + container.ActualWidth - viewportWidth, null, null);
        }
    }

    // The button handles the press itself, so the drag handler listens for handled presses too.
    private void TabButton_Loaded(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.RemoveHandler(PointerPressedEvent, (PointerEventHandler)TabButton_PointerPressed);
        button.AddHandler(PointerPressedEvent, (PointerEventHandler)TabButton_PointerPressed, handledEventsToo: true);
    }

    private void TabButton_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var button = (FrameworkElement)sender;
        if (button.DataContext is BrowserTabViewModel tab)
        {
            _dragHandler.OnTabPressed(tab, e);
        }
    }

    private void TabButton_Click(object sender, RoutedEventArgs e)
    {
        // The release that ends a drag raises a click on the dragged tab before the drag handler hears the release.
        if (_dragHandler.IsDragging)
        {
            return;
        }

        var button = (FrameworkElement)sender;
        if (button.DataContext is BrowserTabViewModel tab)
        {
            ViewModel?.ChooseTab(tab);
        }
    }

    private void CloseTabButton_Loaded(object sender, RoutedEventArgs e)
    {
        var button = (FrameworkElement)sender;
        string closeTooltip = _stringLocalizer.GetString("Browser_TabBar_CloseTabTooltip");
        ToolTipService.SetToolTip(button, closeTooltip);
        AutomationProperties.SetName(button, closeTooltip);
    }

    private void CloseTabButton_Click(object sender, RoutedEventArgs e)
    {
        var button = (FrameworkElement)sender;
        if (button.DataContext is BrowserTabViewModel tab)
        {
            TabCloseRequested?.Invoke(this, tab);
        }
    }

    private void NewTabButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.OpenBlankTab();
    }
}
