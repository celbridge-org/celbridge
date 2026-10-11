using Celbridge.UserInterface;
using Celbridge.WebHost;

namespace Celbridge.Automation.Services;

internal class AutomationService : IAutomationService
{
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly INativeControlReader _nativeControlReader;
    private readonly IWebViewToolBridge _toolBridge;

    public AutomationService(
        IUserInterfaceService userInterfaceService,
        INativeControlReader nativeControlReader,
        IWebViewToolBridge toolBridge)
    {
        _userInterfaceService = userInterfaceService;
        _nativeControlReader = nativeControlReader;
        _toolBridge = toolBridge;
    }

    public Task<Result<ControlSnapshot>> GetControlsAsync()
    {
        return RunOnUIThreadAsync(GetControls);
    }

    public Task<Result<ControlInvocation>> InvokeControlAsync(Func<ControlInfo, bool> match)
    {
        return RunOnUIThreadAsync(() => InvokeControl(match));
    }

    public async Task<Result<PageElementSnapshot>> FindPageElementsAsync(ResourceKey resource, QueryOptions options)
    {
        // Locating can wait for the page to load, so the web view's frame is read only after the page responds.
        var locateResult = await _toolBridge.LocateAsync(resource, options);
        if (locateResult.IsFailure)
        {
            return Result.Fail(locateResult);
        }
        var locateJson = locateResult.Value;

        return await RunOnUIThreadAsync(() => PlacePageElements(resource, locateJson));
    }

    private Result<ControlSnapshot> GetControls()
    {
        if (_userInterfaceService.XamlRoot is not XamlRoot xamlRoot)
        {
            return Result.Fail("The application has no window content to read.");
        }

        var controls = new List<ControlInfo>();
        foreach (var showingControl in ReadControls(xamlRoot))
        {
            controls.Add(showingControl.Info);
        }

        var snapshot = new ControlSnapshot(
            controls,
            xamlRoot.Size.Width,
            xamlRoot.Size.Height,
            xamlRoot.RasterizationScale);

        return snapshot;
    }

    private Result<ControlInvocation> InvokeControl(Func<ControlInfo, bool> match)
    {
        if (_userInterfaceService.XamlRoot is not XamlRoot xamlRoot)
        {
            return Result.Fail("The application has no window content to search.");
        }

        foreach (var showingControl in ReadControls(xamlRoot))
        {
            var control = showingControl.Info;
            if (!control.IsEnabled ||
                !match(control))
            {
                continue;
            }

            var action = showingControl.PerformDefaultAction();
            if (action is null)
            {
                continue;
            }

            var invocation = new ControlInvocation(control, action.Value);
            return invocation;
        }

        return Result.Fail("No showing, enabled control that matches has a default action.");
    }

    private Result<PageElementSnapshot> PlacePageElements(ResourceKey resource, string locateJson)
    {
        if (_userInterfaceService.XamlRoot is not XamlRoot xamlRoot)
        {
            return Result.Fail("The application has no window content to place the page in.");
        }

        var webViewBounds = FindWebViewBounds(xamlRoot, resource);
        if (webViewBounds is null)
        {
            return Result.Fail($"The web view of '{resource}' is not showing, so its page has no frame in the window.");
        }

        var placeResult = PageElementPlacement.Place(locateJson, webViewBounds, xamlRoot.RasterizationScale);
        if (placeResult.IsFailure)
        {
            return Result.Fail(placeResult);
        }
        var location = placeResult.Value;

        var snapshot = new PageElementSnapshot(
            location.Frame,
            location.TotalMatches,
            location.Elements,
            webViewBounds,
            location.DevicePixelRatio,
            xamlRoot.Size.Width,
            xamlRoot.Size.Height,
            xamlRoot.RasterizationScale);

        return snapshot;
    }

    // A document's web view uses the document's resource key as its automation ID. The walk returns only web views.
    private ControlBounds? FindWebViewBounds(XamlRoot xamlRoot, ResourceKey resource)
    {
        var automationId = resource.ToString();
        var webViews = VisualTreeReader.ReadControls(xamlRoot, _nativeControlReader, element => element is WebView2);
        foreach (var showingControl in webViews)
        {
            var control = showingControl.Info;
            if (control.AutomationId == automationId)
            {
                return control.Bounds;
            }
        }

        return null;
    }

    private IEnumerable<ShowingControl> ReadControls(XamlRoot xamlRoot)
    {
        foreach (var showingControl in VisualTreeReader.ReadControls(xamlRoot, _nativeControlReader))
        {
            yield return showingControl;
        }

        foreach (var showingControl in _nativeControlReader.ReadControls())
        {
            yield return showingControl;
        }
    }

    // Tool requests arrive on a background thread, but only the UI thread can read the visual tree. This uses the
    // dispatcher rather than the command queue, so a call still returns while a modal dialog blocks the queue.
    private Task<Result<T>> RunOnUIThreadAsync<T>(Func<Result<T>> operation) where T : notnull
    {
        if (_userInterfaceService.MainWindow is not Window mainWindow)
        {
            return Task.FromResult<Result<T>>(Result.Fail("The application has no main window to read."));
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
                    Result<T>.Fail("The automation call failed on the UI thread").WithException(ex));
            }
        });

        if (!enqueued)
        {
            return Task.FromResult<Result<T>>(Result.Fail("Failed to dispatch the automation call to the UI thread."));
        }

        return completionSource.Task;
    }
}
