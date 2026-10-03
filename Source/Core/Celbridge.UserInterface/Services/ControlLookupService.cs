using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Windows.Foundation;

namespace Celbridge.UserInterface.Services;

public class ControlLookupService : IControlLookupService
{
    private readonly IUserInterfaceService _userInterfaceService;

    public ControlLookupService(IUserInterfaceService userInterfaceService)
    {
        _userInterfaceService = userInterfaceService;
    }

    public Task<Result<ControlLookupResult>> FindControlsAsync(ControlQuery query)
    {
        if (ControlQueryMatcher.IsEmpty(query))
        {
            return Task.FromResult<Result<ControlLookupResult>>(
                Result.Fail("Name at least one of the automation ID, the name and the control type to match."));
        }

        return RunOnUIThreadAsync(() => FindControls(query));
    }

    public Task<Result<ControlInvocation>> InvokeControlAsync(ControlQuery query)
    {
        if (ControlQueryMatcher.IsEmpty(query))
        {
            return Task.FromResult<Result<ControlInvocation>>(
                Result.Fail("Name at least one of the automation ID, the name and the control type to match."));
        }

        return RunOnUIThreadAsync(() => InvokeControl(query));
    }

    private Result<ControlLookupResult> FindControls(ControlQuery query)
    {
        if (_userInterfaceService.XamlRoot is not XamlRoot xamlRoot)
        {
            return Result.Fail("The application has no window content to search.");
        }

        var controls = new List<ControlInfo>();
        foreach (var (element, peer) in ShowingControls(xamlRoot))
        {
            var control = Describe(element, peer);
            if (ControlQueryMatcher.Matches(query, control))
            {
                controls.Add(control);
            }
        }

        var result = new ControlLookupResult(
            controls,
            xamlRoot.Size.Width,
            xamlRoot.Size.Height,
            xamlRoot.RasterizationScale);

        return result;
    }

    private Result<ControlInvocation> InvokeControl(ControlQuery query)
    {
        if (_userInterfaceService.XamlRoot is not XamlRoot xamlRoot)
        {
            return Result.Fail("The application has no window content to search.");
        }

        foreach (var (element, peer) in ShowingControls(xamlRoot))
        {
            var control = Describe(element, peer);
            if (!control.IsEnabled ||
                !ControlQueryMatcher.Matches(query, control))
            {
                continue;
            }

            var action = PerformDefaultAction(peer);
            if (action is null)
            {
                continue;
            }

            var invocation = new ControlInvocation(control, action.Value);
            return invocation;
        }

        return Result.Fail("No showing, enabled control that matches has a default action.");
    }

    /// <summary>
    /// Every element with a size and an automation peer, in the window's content and then in each open popup. The
    /// walk does not enter a collapsed element.
    /// </summary>
    private static IEnumerable<(FrameworkElement Element, AutomationPeer Peer)> ShowingControls(XamlRoot xamlRoot)
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
                var peer = FrameworkElementAutomationPeer.FromElement(frameworkElement) ??
                    FrameworkElementAutomationPeer.CreatePeerForElement(frameworkElement);
                if (peer is not null)
                {
                    yield return (frameworkElement, peer);
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

        var value = (peer.GetPattern(PatternInterface.Value) as IValueProvider)?.Value;

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

    // The visual tree belongs to the UI thread, and the caller is a tool request arriving on its own thread. The
    // dispatcher is used rather than the command queue, so a lookup still answers while a modal dialog holds the
    // queue.
    private Task<Result<T>> RunOnUIThreadAsync<T>(Func<Result<T>> operation) where T : notnull
    {
        if (_userInterfaceService.MainWindow is not Window mainWindow)
        {
            return Task.FromResult<Result<T>>(Result.Fail("The application has no main window to search."));
        }

        var dispatcherQueue = mainWindow.DispatcherQueue;
        if (dispatcherQueue.HasThreadAccess)
        {
            return Task.FromResult(operation());
        }

        var completionSource = new TaskCompletionSource<Result<T>>();
        var enqueued = dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                completionSource.TrySetResult(operation());
            }
            catch (Exception ex)
            {
                completionSource.TrySetResult(
                    Result<T>.Fail("The control lookup failed on the UI thread").WithException(ex));
            }
        });

        if (!enqueued)
        {
            return Task.FromResult<Result<T>>(Result.Fail("Failed to dispatch the control lookup to the UI thread."));
        }

        return completionSource.Task;
    }
}
