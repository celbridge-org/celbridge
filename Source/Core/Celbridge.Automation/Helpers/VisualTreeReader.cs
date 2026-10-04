using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Windows.Foundation;

namespace Celbridge.Automation;

/// <summary>
/// Describes a web view as a pane whose class name is the WebView2 type's full name.
/// </summary>
// UNO-BUG: Uno's WebView2 creates no automation peer.
internal class WebViewAutomationPeer : FrameworkElementAutomationPeer
{
    public WebViewAutomationPeer(WebView2 owner) : base(owner) { }

    protected override string GetClassNameCore() => typeof(WebView2).FullName ?? nameof(WebView2);

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
}

/// <summary>
/// Reads the application's own controls from the visual tree, as their automation peers describe them.
/// </summary>
internal static class VisualTreeReader
{
    /// <summary>
    /// Every element with a size and an automation peer, in the window's content and then in each open popup. An
    /// element drawn by a native view takes that view's frame. The walk does not enter a collapsed element, or an
    /// element whose native view is not showing.
    /// </summary>
    public static IEnumerable<ShowingControl> ReadControls(XamlRoot xamlRoot, INativeControlReader nativeControlReader)
    {
        var roots = new List<DependencyObject>();
        if (xamlRoot.Content is not null)
        {
            roots.Add(xamlRoot.Content);
        }

        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot))
        {
            if (popup.Child is not null)
            {
                roots.Add(popup.Child);
            }
        }

        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<DependencyObject>();
        for (int index = roots.Count - 1; index >= 0; index--)
        {
            pending.Push(roots[index]);
        }

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            if (current is UIElement element &&
                element.Visibility != Visibility.Visible)
            {
                continue;
            }

            if (current is FrameworkElement frameworkElement &&
                frameworkElement.ActualWidth > 0 &&
                frameworkElement.ActualHeight > 0)
            {
                var peer = FindPeer(frameworkElement);
                if (peer is not null)
                {
                    var info = Describe(frameworkElement, peer);

                    var nativeView = nativeControlReader.FindNativeView(frameworkElement);
                    if (nativeView is not null)
                    {
                        if (!nativeView.IsShowing)
                        {
                            continue;
                        }
                        info = info with { Bounds = nativeView.Bounds };
                    }

                    yield return new ShowingControl(info, () => PerformDefaultAction(peer));
                }
            }

            // Pushed in reverse, so children come off the stack in the order they appear.
            int childCount = VisualTreeHelper.GetChildrenCount(current);
            for (int index = childCount - 1; index >= 0; index--)
            {
                pending.Push(VisualTreeHelper.GetChild(current, index));
            }
        }
    }

    private static AutomationPeer? FindPeer(FrameworkElement element)
    {
        var peer = FrameworkElementAutomationPeer.FromElement(element) ??
            FrameworkElementAutomationPeer.CreatePeerForElement(element);
        if (peer is null &&
            element is WebView2 webView)
        {
            peer = new WebViewAutomationPeer(webView);
        }

        return peer;
    }

    /// <summary>
    /// Performs the first default action the peer supports, in the order Invoke, Toggle, Expand and Select.
    /// Returns null when the peer supports none of them.
    /// </summary>
    private static ControlAction? PerformDefaultAction(AutomationPeer peer)
    {
        if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
        {
            invoke.Invoke();
            return ControlAction.Invoke;
        }

        if (peer.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle)
        {
            toggle.Toggle();
            return ControlAction.Toggle;
        }

        if (peer.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider expandCollapse)
        {
            expandCollapse.Expand();
            return ControlAction.Expand;
        }

        if (peer.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider selectionItem)
        {
            selectionItem.Select();
            return ControlAction.Select;
        }

        return null;
    }

    private static ControlInfo Describe(FrameworkElement element, AutomationPeer peer)
    {
        // An element without an automation ID of its own is known by its name, as UI Automation reports it.
        var automationId = peer.GetAutomationId();
        if (string.IsNullOrEmpty(automationId))
        {
            automationId = element.Name;
        }

        var transform = element.TransformToVisual(null);
        var frame = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        var bounds = new ControlBounds(frame.X, frame.Y, frame.Width, frame.Height);

        var value = ReadValue(element, peer);

        var control = new ControlInfo(
            automationId ?? string.Empty,
            peer.GetName() ?? string.Empty,
            peer.GetAutomationControlType().ToString(),
            peer.GetClassName() ?? string.Empty,
            bounds,
            peer.IsEnabled(),
            CheckedState(peer),
            value);

        return control;
    }

    /// <summary>
    /// Returns the control's value from its peer's value provider, or null if the peer has none. WinUI's TextBox
    /// peer doesn't expose a value provider to managed code, so for a TextBox this returns its Text.
    /// </summary>
    private static string? ReadValue(FrameworkElement element, AutomationPeer peer)
    {
        if (peer.GetPattern(PatternInterface.Value) is IValueProvider valueProvider)
        {
            return valueProvider.Value;
        }

        if (element is TextBox textBox)
        {
            return textBox.Text;
        }

        return null;
    }

    private static bool? CheckedState(AutomationPeer peer)
    {
        if (peer.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle)
        {
            return toggle.ToggleState == ToggleState.On;
        }

        if (peer.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider selectionItem)
        {
            return selectionItem.IsSelected;
        }

        return null;
    }
}
