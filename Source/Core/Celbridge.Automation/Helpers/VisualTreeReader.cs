using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Windows.Foundation;

namespace Celbridge.Automation;

/// <summary>
/// Describes a web view as a pane whose class name is the WebView2 type's full name.
/// </summary>
// UNO-BUG: Uno's WebView2 has no automation peer of its own. An unnamed one gets no peer, and a named one gets a
// generic peer that reports neither its role nor its type.
internal class WebViewAutomationPeer : FrameworkElementAutomationPeer
{
    public static readonly string WebViewClassName = typeof(WebView2).FullName ?? nameof(WebView2);

    public WebViewAutomationPeer(WebView2 owner) : base(owner) { }

    protected override string GetClassNameCore() => WebViewClassName;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
}

/// <summary>
/// Reads the application's own controls from the visual tree, as their automation peers describe them.
/// </summary>
internal static class VisualTreeReader
{
    /// <summary>
    /// Returns every element that has a size and an automation peer, first in the window's content and then in each
    /// open popup. An element drawn by a native view uses that view's frame. The walk skips collapsed elements and
    /// elements whose native view is hidden, along with their children. When include is given, only the elements
    /// it accepts are returned, but the walk still visits the children of the others.
    /// </summary>
    public static IEnumerable<ShowingControl> ReadControls(
        XamlRoot xamlRoot,
        INativeControlReader nativeControlReader,
        Func<FrameworkElement, bool>? include = null)
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

        var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot) as DependencyObject;

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
                frameworkElement.ActualHeight > 0 &&
                (include is null || include(frameworkElement)))
            {
                var peer = FindPeer(frameworkElement);
                if (peer is not null)
                {
                    var hasKeyboardFocus = ReferenceEquals(frameworkElement, focusedElement);
                    var info = Describe(frameworkElement, peer, hasKeyboardFocus);

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
        if (element is WebView2 webView &&
            peer?.GetClassName() != WebViewAutomationPeer.WebViewClassName)
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

    private static ControlInfo Describe(FrameworkElement element, AutomationPeer peer, bool hasKeyboardFocus)
    {
        // UI Automation identifies an element that has no automation ID by its name, so this does the same.
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
            value,
            hasKeyboardFocus);

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
