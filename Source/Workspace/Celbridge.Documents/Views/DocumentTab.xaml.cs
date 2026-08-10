using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using Celbridge.Platform;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.UserInterface.Services;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml.Media.Animation;

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
    MoveToPrimarySection,
    MoveToSecondarySection,
    CopyResourceKey,
    CopyFilePath,
    SelectFile,
    OpenFileExplorer,
    OpenApplication,
    RestoreChrome,
    Reopen,
    ReopenWith
}

// Defined in XAML rather than as a C# Markup subclass of TabViewItem: a derived Markup class throws on
// instantiation, however simple the derived class is.
public partial class DocumentTab : TabViewItem
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly IPlatformInfo _platformInfo;

    // The currently running attention flash, if any. Kept so a repeated flash restarts cleanly.
    private Storyboard? _attentionStoryboard;

    public DocumentTabViewModel ViewModel { get; }

    /// <summary>
    /// The section this tab belongs to. Set by DocumentSectionView when the tab is added.
    /// </summary>
    public DocumentSection Section { get; set; }

    /// <summary>
    /// Whether this tab's area is currently split, so it has a sibling section to move to. Set by
    /// DocumentSectionView.
    /// </summary>
    public bool IsAreaSplit { get; set; }

    /// <summary>
    /// Gets whether this tab is the active document.
    /// </summary>
    public bool IsActiveDocument { get; private set; }

    /// <summary>
    /// Briefly pulses the tab's background to the accent color to draw the user's attention to it, then fades
    /// it back out. Gives visible feedback when a tab is opened, surfaced, or changes address (moved to another
    /// section or reordered within one).
    /// </summary>
    public void FlashAttention()
    {
        _attentionStoryboard?.Stop();
        _attentionStoryboard = AttentionFlash.Play(AttentionOverlay);
    }

    /// <summary>
    /// Flashes the tab after the next layout pass, so a header that was just moved, inserted, or reordered has
    /// settled into the strip before it pulses.
    /// </summary>
    public void FlashAttentionDeferred()
    {
        _ = DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            FlashAttention);
    }

    /// <summary>
    /// Event raised when a context menu action is triggered.
    /// </summary>
    public event Action<DocumentTab, DocumentTabMenuAction>? ContextMenuActionRequested;

    /// <summary>
    /// Event raised when this tab starts being dragged.
    /// </summary>
    public event Action<DocumentTab>? DragStarted;

    public DocumentTab()
    {
        this.InitializeComponent();

        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _platformInfo = ServiceLocator.AcquireService<IPlatformInfo>();
        ViewModel = ServiceLocator.AcquireService<DocumentTabViewModel>();

        // The context menu opens over the document area, where a hosted web view would take the click too.
        var overlayInputSuppressor = ServiceLocator.AcquireService<IOverlayInputSuppressor>();
        overlayInputSuppressor.SuppressWhileOpen(TabContextMenu);

        CloseMenuItem.Text = _stringLocalizer.GetString("DocumentTab_Close");
        CloseOthersMenuItem.Text = _stringLocalizer.GetString("DocumentTab_CloseOthers");
        CloseToTheRightMenuItem.Text = _stringLocalizer.GetString("DocumentTab_CloseRight");
        CloseToTheLeftMenuItem.Text = _stringLocalizer.GetString("DocumentTab_CloseLeft");
        CloseAllMenuItem.Text = _stringLocalizer.GetString("DocumentTab_CloseAll");
        ApplyMoveMenuLabels();
        CopyResourceKeyMenuItem.Text = _stringLocalizer.GetString("DocumentTab_CopyResourceKey");
        CopyFilePathMenuItem.Text = _stringLocalizer.GetString("DocumentTab_CopyFilePath");
        SelectFileMenuItem.Text = _stringLocalizer.GetString("DocumentTab_SelectFile");
        string fileManagerName = _stringLocalizer.GetString(_platformInfo.FileManagerNameStringKey);
        OpenFileExplorerMenuItem.Text = _stringLocalizer.GetString("DocumentTab_OpenFileManager", fileManagerName);
        OpenApplicationMenuItem.Text = _stringLocalizer.GetString("DocumentTab_OpenApplication");
        ReopenMenuItem.Text = _stringLocalizer.GetString("DocumentTab_Reopen");
        ReopenWithMenuItem.Text = _stringLocalizer.GetString("DocumentTab_ReopenWith");

        ApplyCloseShortcutHints();
    }

    // Labels the two move options for the split orientation of this tab's area: left and right for Main
    // and Bottom, up and down for the vertically split Side area.
    private void ApplyMoveMenuLabels()
    {
        bool splitsHorizontally = Section.GetArea().SplitsHorizontally();

        string primaryKey = splitsHorizontally ? "DocumentTab_MoveLeft" : "DocumentTab_MoveUp";
        string secondaryKey = splitsHorizontally ? "DocumentTab_MoveRight" : "DocumentTab_MoveDown";

        MoveToPrimarySectionMenuItem.Text = _stringLocalizer.GetString(primaryKey);
        MoveToSecondarySectionMenuItem.Text = _stringLocalizer.GetString(secondaryKey);
    }

    // Displays the close shortcut hints next to the Close and Close All menu items. These are display-only
    // labels matching the shortcuts handled in KeyboardShortcutService.
    private void ApplyCloseShortcutHints()
    {
        bool usesCommandModifier = _platformInfo.CommandModifier == CommandModifierKey.Command;
        string closeAllHintKey = usesCommandModifier ? "DocumentTab_CloseAllShortcutCommand" : "DocumentTab_CloseAllShortcutControl";

        CloseMenuItem.KeyboardAcceleratorTextOverride = GetCloseShortcutHint();
        CloseAllMenuItem.KeyboardAcceleratorTextOverride = _stringLocalizer.GetString(closeAllHintKey);
    }

    // The display form of the close-document shortcut for the current platform: the Command-glyph form on macOS,
    // the "Ctrl" form on Windows. Matches the shortcut handled in KeyboardShortcutService.
    private string GetCloseShortcutHint()
    {
        bool usesCommandModifier = _platformInfo.CommandModifier == CommandModifierKey.Command;
        string closeHintKey = usesCommandModifier ? "DocumentTab_CloseShortcutCommand" : "DocumentTab_CloseShortcutControl";

        return _stringLocalizer.GetString(closeHintKey);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        OverrideCloseButtonTooltip();
    }

    // Uno's TabViewItem seeds the close button with a hardcoded "Close tab (Ctrl+F4)" tooltip that ignores the
    // platform and the app's actual binding. Replace it with the close shortcut the app binds, so macOS shows the
    // Command glyph and both platforms match the close hint on the tab context menu.
    private void OverrideCloseButtonTooltip()
    {
        if (GetTemplateChild("CloseButton") is not Button closeButton)
        {
            return;
        }

        string shortcutHint = GetCloseShortcutHint();
        string tooltipText = _stringLocalizer.GetString("DocumentTab_CloseTabTooltip", shortcutHint);
        ToolTipService.SetToolTip(closeButton, tooltipText);
    }

    /// <summary>
    /// Updates the visual state to indicate whether this tab is the active document.
    /// </summary>
    public void UpdateActiveDocumentState(bool isActiveDocument)
    {
        if (IsActiveDocument == isActiveDocument)
        {
            return;
        }

        IsActiveDocument = isActiveDocument;
        SelectionIndicator.Visibility = isActiveDocument ? Visibility.Visible : Visibility.Collapsed;
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

    private void ContextMenu_MoveToPrimarySection(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.MoveToPrimarySection);
    }

    private void ContextMenu_MoveToSecondarySection(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.MoveToSecondarySection);
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

    private void ContextMenu_RestoreChrome(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.RestoreChrome);
    }

    private void ContextMenu_Reopen(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.Reopen);
    }

    private void ContextMenu_ReopenWith(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.ReopenWith);
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

        // Move options only apply within a split area, and only in the direction that has a sibling
        // section. The labels follow the area's split orientation, so a vertically split Side area
        // offers Move Up and Move Down.
        bool isSecondarySection = Section.IsSecondarySection();
        bool canMoveToPrimary = IsAreaSplit && isSecondarySection;
        bool canMoveToSecondary = IsAreaSplit && !isSecondarySection;

        ApplyMoveMenuLabels();

        MoveToPrimarySectionMenuItem.Visibility = canMoveToPrimary ? Visibility.Visible : Visibility.Collapsed;
        MoveToSecondarySectionMenuItem.Visibility = canMoveToSecondary ? Visibility.Visible : Visibility.Collapsed;

        // Show the separator only if at least one move option is visible
        MoveSeparator.Visibility = (canMoveToPrimary || canMoveToSecondary) ? Visibility.Visible : Visibility.Collapsed;

        // A utility tab presents a docked utility, not a file, so hide the options that reveal or act on its
        // backing file. The close and move options remain.
        bool isUtility = ViewModel.IsUtility;
        var fileActionsVisibility = isUtility ? Visibility.Collapsed : Visibility.Visible;
        SelectFileSeparator.Visibility = fileActionsVisibility;
        SelectFileMenuItem.Visibility = fileActionsVisibility;
        CopySeparator.Visibility = fileActionsVisibility;
        CopyResourceKeyMenuItem.Visibility = fileActionsVisibility;
        CopyFilePathMenuItem.Visibility = fileActionsVisibility;
        OpenSeparator.Visibility = fileActionsVisibility;
        OpenFileExplorerMenuItem.Visibility = fileActionsVisibility;
        OpenApplicationMenuItem.Visibility = fileActionsVisibility;

        // A view that has hidden the chrome carrying its own controls has no way back, so the shared menu
        // offers one. The view supplies the text, so this menu never names a particular kind of chrome.
        bool canRestoreChrome = false;
        if (Content is IDocumentChromeOwner chromeOwner &&
            chromeOwner.CanRestoreChrome)
        {
            canRestoreChrome = true;
            RestoreChromeMenuItem.Text = _stringLocalizer.GetString(chromeOwner.RestoreChromeMenuTextKey);
        }

        RestoreChromeSeparator.Visibility = canRestoreChrome ? Visibility.Visible : Visibility.Collapsed;
        RestoreChromeMenuItem.Visibility = canRestoreChrome ? Visibility.Visible : Visibility.Collapsed;

        // A utility tab hosts a docked utility, not a file opened with a chosen editor. Reopening would dock it
        // back into the panel and then open a second, uncontrolled instance, so the reopen options are hidden
        // for utilities.
        ReopenSeparator.Visibility = isUtility ? Visibility.Collapsed : Visibility.Visible;
        ReopenMenuItem.Visibility = isUtility ? Visibility.Collapsed : Visibility.Visible;

        // Show "Reopen with..." only when there are multiple editors registered for this file type.
        bool showReopenWith = !isUtility
            && ViewModel.HasMultipleCompatibleEditors();
        ReopenWithMenuItem.Visibility = showReopenWith ? Visibility.Visible : Visibility.Collapsed;
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
        // Focus shows the active document on its own, so make this tab active before toggling rather
        // than relying on the first tap of the double having already done it.
        _messengerService.Send(new DocumentViewFocusedMessage(ViewModel.FileResource));

        _commandService.Execute<ISetLayoutCommand>(command =>
        {
            command.Transition = LayoutTransition.ToggleFocus;
        });
        e.Handled = true;
    }

    private void DocumentTab_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Send message to notify that this tab was clicked - this updates the active document
        var message = new DocumentViewFocusedMessage(ViewModel.FileResource);
        _messengerService.Send(message);

        // Focus the document when the tab is clicked (even if tab is already selected). The view gives its
        // web content keyboard focus and reports it, releasing the previously focused surface.
        _ = this.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            var documentView = this.Content as IDocumentView;
            documentView?.FocusDocument();
        });
    }

    private void DocumentTab_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        // Notify that this tab is being dragged
        DragStarted?.Invoke(this);
    }
}
