using Celbridge.Logging;
using Celbridge.UserInterface;
using Celbridge.WebHost;

// The Uno SDK's implicit global usings include System.Windows.Input, which on the Windows head also
// contains a FocusManager type, so the bare name is ambiguous there.
using FocusManager = Microsoft.UI.Xaml.Input.FocusManager;

namespace Celbridge.WorkspaceUI.Services;

/// <summary>
/// Observes every managed focus change in the window and reports the focused panel to the focus
/// service, classifying each focused element by its nearest ancestor declaring FocusTracking.Panel.
/// </summary>
public class PanelFocusTracker
{
    private readonly IFocusService _focusService;
    private readonly IWebViewFocusRegistry _webViewFocusRegistry;
    private readonly ILogger<PanelFocusTracker> _logger;
    private bool _isStarted;

    public PanelFocusTracker(
        IFocusService focusService,
        IWebViewFocusRegistry webViewFocusRegistry,
        ILogger<PanelFocusTracker> logger)
    {
        _focusService = focusService;
        _webViewFocusRegistry = webViewFocusRegistry;
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
            return;
        }

        // A deliberate grant is holding the panel against the tail of the gesture that triggered it.
        if (FocusIntent.IsPanelClaimSuppressed)
        {
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

        // Walk towards the visual root, taking the nearest Panel declaration. No declaration
        // classifies as None, which clears panel focus but preserves the edit context.
        var panel = WorkspacePanelId.None;
        IEditTarget? editTarget = null;
        var foundDeclaration = false;
        var reachedMainContentRoot = false;

        DependencyObject? current = element;
        while (current is not null)
        {
            if (!foundDeclaration)
            {
                var declaredPanel = FocusTracking.GetPanel(current);
                if (declaredPanel != WorkspacePanelId.None)
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

        _logger.LogTrace(
            "Managed focus moved to {Element}, classified as {Panel}",
            element.GetType().Name,
            panel);

        // The focus service treats a repeated report for the current panel and target as a no-op,
        // so intra-panel focus moves do not spam it.
        var claim = FocusClaim.FromManagedControl(panel, editTarget);
        _focusService.OnFocusReceived(claim);
    }
}
