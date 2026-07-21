using Celbridge.UserInterface;

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
    private bool _isStarted;

    public PanelFocusTracker(IFocusService focusService)
    {
        _focusService = focusService;
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

        // Only user-driven focus (Pointer, Keyboard) reports a panel. Programmatic focus is the app
        // moving focus for itself, and the caller already knows what it did: intra-panel restoration
        // (re-focusing a list item after a rename, a dialog auto-focusing its first field) would only
        // echo the current panel, and programmatically focusing a hosted WebView is the broken managed
        // routing path we avoid on macOS anyway. Web surfaces report their real focus through the
        // web-view focus registry instead. Guard on "not Programmatic" rather than "is Pointer or
        // Keyboard" because real clicks can transition through Unfocused.
        if (element is Control control &&
            control.FocusState == FocusState.Programmatic)
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
        var panel = WorkspacePanel.None;
        IEditTarget? editTarget = null;
        var foundDeclaration = false;
        var reachedMainContentRoot = false;

        DependencyObject? current = element;
        while (current is not null)
        {
            if (!foundDeclaration)
            {
                var declaredPanel = FocusTracking.GetPanel(current);
                if (declaredPanel != WorkspacePanel.None)
                {
                    panel = declaredPanel;
                    editTarget = FocusTracking.GetEditTarget(current);
                    foundDeclaration = true;
                }
                else if (FocusTracking.GetPreservePanelFocus(current))
                {
                    // Focus landed on chrome marked to preserve panel focus. Such an element can hold focus
                    // transiently (e.g. as the fallback sink during dialog teardown or a tree rebuild)
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

        // The focus service treats a repeated report for the current panel and target as a no-op,
        // so intra-panel focus moves do not spam it.
        _focusService.OnFocusReceived(panel, editTarget);
    }
}
