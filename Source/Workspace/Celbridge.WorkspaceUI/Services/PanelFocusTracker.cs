using Celbridge.Documents;
using Celbridge.Logging;
using Celbridge.UserInterface;
using Celbridge.WebHost;

// The Uno SDK's implicit global usings include System.Windows.Input, which on the Windows head also
// contains a FocusManager type, so the bare name is ambiguous there.
using FocusManager = Microsoft.UI.Xaml.Input.FocusManager;

namespace Celbridge.WorkspaceUI.Services;

/// <summary>
/// Observes every managed focus change in the window and reports what the focused element belongs to: the
/// panel, taken from its nearest ancestor declaring FocusTracking.Panel, and the document it sits in, taken
/// from its nearest IDocumentView ancestor.
///
/// It also enforces the panel hold. A click keeps producing focus events for a few milliseconds after the
/// work it triggered has finished, so opening a document can be followed by the Explorer tree quietly taking
/// the keyboard back. Three things share the job of surviving that, and all three are needed: the hold
/// (IFocusService.HoldPanelUntilNextInput) says which panel the keyboard was just given to, this class
/// declines to report anything else while the hold lasts and moves the keyboard back to that panel, and
/// IWebViewFocusRegistry ignores the blur a web page reports because of that round trip.
/// </summary>
public class PanelFocusTracker
{
    private readonly IFocusService _focusService;
    private readonly IWebViewFocusRegistry _webViewFocusRegistry;
    private readonly IFocusReconciler _focusReconciler;
    private readonly IMessengerService _messengerService;
    private readonly ILogger<PanelFocusTracker> _logger;
    private bool _isStarted;

    // Set for the duration of the hand-back below. The web-surface path moves the keyboard while this call is
    // still on the stack, which raises the focus events this class listens to, so without the flag the
    // hand-back would set itself off again. The other path defers its move and does not need the flag: the
    // keyboard lands on the held panel, which is reported rather than handed back.
    private bool _isHandingKeyboardBack;

    public PanelFocusTracker(
        IFocusService focusService,
        IWebViewFocusRegistry webViewFocusRegistry,
        IFocusReconciler focusReconciler,
        IMessengerService messengerService,
        ILogger<PanelFocusTracker> logger)
    {
        _focusService = focusService;
        _webViewFocusRegistry = webViewFocusRegistry;
        _focusReconciler = focusReconciler;
        _messengerService = messengerService;
        _logger = logger;
    }

    public void Start()
    {
        if (_isStarted)
        {
            return;
        }

        _isStarted = true;
        FocusManager.GotFocus += OnGotFocus;
    }

    private void OnGotFocus(object? sender, FocusManagerGotFocusEventArgs e)
    {
        if (e.NewFocusedElement is not UIElement element)
        {
            return;
        }

        // Focus the application restores for its own housekeeping (re-focusing a list item after a
        // tree rebuild, returning focus after an inline edit) must not be reclassified as the user
        // moving panels. FocusState cannot carry that distinction because Uno reports programmatic
        // focus back as Pointer, so restoration call sites declare themselves through FocusIntent.
        if (FocusIntent.IsRestorationInProgress)
        {
            _logger.LogTrace(
                "Managed focus moved to {Element}, not reported: a restoration is in progress",
                element.GetType().Name);
            return;
        }

        // A web surface reports through the registry, which alone can supply the callback that releases
        // the surface later. On the packaged Windows head the web view also takes managed focus, and a
        // report classified from the visual tree here would carry no such callback, so it would read as
        // managed chrome claiming the keyboard and release the surface that had just taken it.
        if (_webViewFocusRegistry.IsRegisteredWebSurface(element))
        {
            return;
        }

        var mainContentRoot = element.XamlRoot?.Content;
        if (mainContentRoot is null)
        {
            return;
        }

        // The document the focused element sits in, which the report below makes the active document. A
        // press that lands on nothing focusable raises no focus change at all, so the section view reports
        // that case from the pointer instead.
        var documentView = FocusTracking.FindDocumentView(element);
        var documentResource = documentView?.FileResource ?? ResourceKey.Empty;

        // Walk towards the visual root, taking the nearest Panel declaration. No declaration
        // classifies as None, which clears panel focus but preserves the edit context.
        var panel = FocusPanelId.None;
        IEditTarget? editTarget = null;
        var foundDeclaration = false;
        var reachedMainContentRoot = false;

        DependencyObject? current = element;
        while (current is not null)
        {
            if (!foundDeclaration)
            {
                var declaredPanel = FocusTracking.GetPanel(current);
                if (declaredPanel != FocusPanelId.None)
                {
                    panel = declaredPanel;
                    editTarget = FocusTracking.GetEditTarget(current);
                    foundDeclaration = true;
                }
                else if (FocusTracking.GetPreservePanelFocus(current))
                {
                    // Focus landed on chrome marked to preserve panel focus. Such an element can hold focus
                    // transiently (e.g. as the focus placeholder during dialog teardown or a tree rebuild)
                    // without representing a move off the panel, so preserve the current panel by not
                    // reporting.
                    return;
                }
            }

            if (ReferenceEquals(current, mainContentRoot))
            {
                reachedMainContentRoot = true;
                break;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        // An element whose walk never passes the main content root is popup-hosted (flyout,
        // context menu, ContentDialog). Popups preserve the previous focus, so it is not reported.
        if (!reachedMainContentRoot)
        {
            return;
        }

        // Opening a document gives it the keyboard and protects its panel for a moment. The click that opened
        // it keeps producing focus events afterwards, and those events really do move the keyboard: the
        // Explorer tree takes it back a few milliseconds after the document has taken it.
        //
        // Focus landing on the protected panel is the document itself arriving, so it is reported as usual.
        // Focus landing anywhere else is one of those leftover events. Saying nothing about it would leave the
        // document looking focused while the user typed into the tree, so move the keyboard back instead.
        var heldPanel = _focusService.HeldPanel;
        if (heldPanel != FocusPanelId.None
            && panel != heldPanel)
        {
            _logger.LogTrace(
                "Managed focus moved to {Element} in {Panel}, not reported: {HeldPanel} is held",
                element.GetType().Name,
                panel,
                heldPanel);

            HandKeyboardBackToHeldPanel(heldPanel);
            return;
        }

        _logger.LogTrace(
            "Managed focus moved to {Element}, classified as {Panel}, document {Document}",
            element.GetType().Name,
            panel,
            documentResource.IsEmpty ? "none" : documentResource.ToString());

        // The focus service treats a repeated report for the current panel and target as a no-op,
        // so intra-panel focus moves do not spam it.
        var claim = FocusClaim.FromManagedControl(panel, editTarget);
        _focusService.OnFocusReceived(claim);

        // Reported after the claim so the activation it drives sees the panel the document belongs to.
        if (!documentResource.IsEmpty)
        {
            var message = new DocumentViewFocusedMessage(documentResource);
            _messengerService.Send(message);
        }
    }

    // Moves the keyboard back to the document that was just opened, after a leftover focus event has taken it
    // somewhere else.
    private void HandKeyboardBackToHeldPanel(FocusPanelId heldPanel)
    {
        if (_isHandingKeyboardBack)
        {
            return;
        }

        _logger.LogDebug("Handing the keyboard back to the held panel {HeldPanel}", heldPanel);

        _isHandingKeyboardBack = true;
        try
        {
            // Two kinds of document, two ways back. A web-based editor lives in a WebView, and its page
            // separately tells the host when it has lost the keyboard. That message is already on its way by
            // the time we get here and would undo the move, so the keyboard is taken back right now rather
            // than on a later pass through the dispatcher. A document built from ordinary controls sends no
            // such message, so asking its panel to focus itself again is enough.
            if (_webViewFocusRegistry.HasFocusedSurface)
            {
                _focusReconciler.Reconcile();
                return;
            }

            _focusService.RefocusPanel(heldPanel);
        }
        finally
        {
            _isHandingKeyboardBack = false;
        }
    }
}
