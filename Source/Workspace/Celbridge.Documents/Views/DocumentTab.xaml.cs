using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using Celbridge.Platform;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
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
    MoveLeft,
    MoveRight,
    CopyResourceKey,
    CopyFilePath,
    SelectFile,
    OpenFileExplorer,
    OpenApplication,
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
    /// The section index (0, 1, or 2) this tab belongs to. Set by DocumentSection when the tab is added.
    /// </summary>
    public int SectionIndex { get; set; }

    /// <summary>
    /// The number of sections currently visible. Set by DocumentSection.
    /// </summary>
    public int VisibleSectionCount { get; set; } = 1;

    /// <summary>
    /// Gets whether this tab is the active document.
    /// </summary>
    public bool IsActiveDocument { get; private set; }

    /// <summary>
    /// Briefly pulses the tab's background to the accent color to draw the user's attention to it, then fades
    /// it back out. Used to give visible feedback when a tab is surfaced (e.g. activating a docked utility) or
    /// moved into a different section by a section-count change.
    /// </summary>
    public void FlashAttention()
    {
        _attentionStoryboard?.Stop();
        _attentionStoryboard = AttentionFlash.Play(AttentionOverlay);
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
        string fileManagerName = _stringLocalizer.GetString(_platformInfo.FileManagerNameStringKey);
        OpenFileExplorerMenuItem.Text = _stringLocalizer.GetString("DocumentTab_OpenFileManager", fileManagerName);
        OpenApplicationMenuItem.Text = _stringLocalizer.GetString("DocumentTab_OpenApplication");
        ReopenMenuItem.Text = _stringLocalizer.GetString("DocumentTab_Reopen");
        ReopenWithMenuItem.Text = _stringLocalizer.GetString("DocumentTab_ReopenWith");

        ApplyCloseShortcutHints();
    }

    // Displays the close shortcut hints next to the Close and Close All menu items. These are display-only
    // labels matching the shortcuts handled in KeyboardShortcutService. The platform command modifier selects
    // between the Command-glyph form shown on macOS and the "Ctrl" form shown on Windows.
    private void ApplyCloseShortcutHints()
    {
        bool usesCommandModifier = _platformInfo.CommandModifier == CommandModifierKey.Command;

        string closeHintKey = usesCommandModifier ? "DocumentTab_CloseShortcutCommand" : "DocumentTab_CloseShortcutControl";
        string closeAllHintKey = usesCommandModifier ? "DocumentTab_CloseAllShortcutCommand" : "DocumentTab_CloseAllShortcutControl";

        CloseMenuItem.KeyboardAcceleratorTextOverride = _stringLocalizer.GetString(closeHintKey);
        CloseAllMenuItem.KeyboardAcceleratorTextOverride = _stringLocalizer.GetString(closeAllHintKey);
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
        // Double-clicking a document tab toggles the Focus layout (side panels hidden)
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
