using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Microsoft.Extensions.Localization;

namespace Celbridge.Documents.Views;

/// <summary>
/// Actions available in the document tab context menu.
/// </summary>
public enum DocumentTabMenuAction
{
    Close,
    CloseOthers,
    CloseOthersRight,
    CloseOthersLeft,
    CloseAll,
    MoveLeft,
    MoveRight,
    CopyResourceKey,
    CopyFilePath,
    SelectFile,
    OpenFileExplorer,
    OpenApplication
}

// I've tried writing this class using a C# Markup class subclassed from TabViewItem, but it didn't work.
// No matter how simple I make the derived class, an exception is thrown when the class is instantiated.
// I've given up for now and am using a XAML file instead.
public partial class DocumentTab : TabViewItem
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;

    public DocumentTabViewModel ViewModel { get; }

    /// <summary>
    /// The section index (0, 1, or 2) this tab belongs to. Set by DocumentSection when the tab is added.
    /// </summary>
    public int SectionIndex { get; set; }

    /// <summary>
    /// The number of sections currently visible. Set by DocumentSection.
    /// </summary>
    public int VisibleSectionCount { get; set; } = 1;

    /// <summary>
    /// Event raised when a context menu action is triggered.
    /// </summary>
    public event Action<DocumentTab, DocumentTabMenuAction>? ContextMenuActionRequested;

    public DocumentTab()
    {
        this.InitializeComponent();

        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();
        ViewModel = ServiceLocator.AcquireService<DocumentTabViewModel>();

        CloseMenuItem.Text = _stringLocalizer.GetString("DocumentTab_Close");
        CloseOthersMenuItem.Text = _stringLocalizer.GetString("DocumentTab_CloseOthers");
        CloseToTheRightMenuItem.Text = _stringLocalizer.GetString("DocumentTab_CloseRight");
        CloseToTheLeftMenuItem.Text = _stringLocalizer.GetString("DocumentTab_CloseLeft");
        CloseAllMenuItem.Text = _stringLocalizer.GetString("DocumentTab_CloseAll");
        MoveLeftMenuItem.Text = _stringLocalizer.GetString("DocumentTab_MoveLeft");
        MoveRightMenuItem.Text = _stringLocalizer.GetString("DocumentTab_MoveRight");
        CopyResourceKeyMenuItem.Text = _stringLocalizer.GetString("DocumentTab_CopyResourceKey");
        CopyFilePathMenuItem.Text = _stringLocalizer.GetString("DocumentTab_CopyFilePath");
        SelectFileMenuItem.Text = _stringLocalizer.GetString("DocumentTab_SelectFile");
        OpenFileExplorerMenuItem.Text = _stringLocalizer.GetString("DocumentTab_OpenFileExplorer");
        OpenApplicationMenuItem.Text = _stringLocalizer.GetString("DocumentTab_OpenApplication");
    }

    private void ContextMenu_Close(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.Close);
    }

    private void ContextMenu_CloseOthers(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.CloseOthers);
    }

    private void ContextMenu_CloseToTheRight(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.CloseOthersRight);
    }

    private void ContextMenu_CloseToTheLeft(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.CloseOthersLeft);
    }

    private void ContextMenu_CloseAll(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.CloseAll);
    }

    private void ContextMenu_MoveLeft(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.MoveLeft);
    }

    private void ContextMenu_MoveRight(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.MoveRight);
    }

    private void ContextMenu_SelectFile(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.SelectFile);
    }

    private void ContextMenu_CopyResourceKey(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.CopyResourceKey);
    }

    private void ContextMenu_CopyFilePath(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.CopyFilePath);
    }

    private void ContextMenu_OpenFileExplorer(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.OpenFileExplorer);
    }

    private void ContextMenu_OpenApplication(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.OpenApplication);
    }

    private void TabContextMenu_Opening(object sender, object e)
    {
        // Find the parent TabView to get tab count and position
        var tabView = FindParentTabView();
        if (tabView is null)
        {
            return;
        }

        int tabCount = tabView.TabItems.Count;
        int tabIndex = tabView.TabItems.IndexOf(this);

        // Only show "Close Others" if there are at least 2 other tabs to close
        CloseOthersMenuItem.Visibility = tabCount > 2 ? Visibility.Visible : Visibility.Collapsed;

        // Only show "Close All" if there are at least 2 tabs to close
        CloseAllMenuItem.Visibility = tabCount > 1 ? Visibility.Visible : Visibility.Collapsed;

        // Only show "Close to the Right" if there are tabs to the right of this tab
        bool hasTabsToRight = tabIndex >= 0 && tabIndex < tabCount - 1;
        CloseToTheRightMenuItem.Visibility = hasTabsToRight ? Visibility.Visible : Visibility.Collapsed;

        // Only show "Close to the Left" if there are tabs to the left of this tab
        bool hasTabsToLeft = tabIndex > 0;
        CloseToTheLeftMenuItem.Visibility = hasTabsToLeft ? Visibility.Visible : Visibility.Collapsed;

        // Show move options only when there are multiple sections
        bool hasMultipleSections = VisibleSectionCount > 1;

        // Show "Move Left" only if there's a section to the left
        bool canMoveLeft = hasMultipleSections && SectionIndex > 0;
        MoveLeftMenuItem.Visibility = canMoveLeft ? Visibility.Visible : Visibility.Collapsed;

        // Show "Move Right" only if there's a section to the right
        bool canMoveRight = hasMultipleSections && SectionIndex < VisibleSectionCount - 1;
        MoveRightMenuItem.Visibility = canMoveRight ? Visibility.Visible : Visibility.Collapsed;

        // Show the separator only if at least one move option is visible
        MoveSeparator.Visibility = (canMoveLeft || canMoveRight) ? Visibility.Visible : Visibility.Collapsed;
    }

    private TabView? FindParentTabView()
    {
        // WinUI does not provide a built-in way to get the parent TabView from a TabViewItem, so
        // we have to walk up the visual tree ourselves.
        DependencyObject? current = this;
        while (current != null)
        {
            if (current is TabView tabView)
            {
                return tabView;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void DocumentTab_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // Double-clicking a document tab toggles fullscreen layout
        _commandService.Execute<ISetLayoutCommand>(command =>
        {
            command.Transition = LayoutTransition.ToggleLayout;
        });
        e.Handled = true;
    }

    private void DocumentTab_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Focus the document editor when the tab is clicked (even if tab is already selected)
        _ = this.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            FocusDocumentEditor();
        });
    }

    private void FocusDocumentEditor()
    {
        var documentView = this.Content as IDocumentView;
        if (documentView is FrameworkElement element)
        {
            // Focus workaround for Monaco editor hosted in WebView2.
            // Try to find a WebView2 control within the document view and focus it.
            var webView = VisualTreeHelperEx.FindDescendant<WebView2>(element);
            if (webView != null)
            {
                webView.Focus(FocusState.Programmatic);
            }
        }
    }
}
