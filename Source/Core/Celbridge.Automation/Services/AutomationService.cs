using System.Text.Json;
using System.Text.Json.Serialization;
using Celbridge.Logging;
using Celbridge.UserInterface;
using Celbridge.WebHost;

namespace Celbridge.Automation.Services;

internal class AutomationService : IAutomationService
{
    private static readonly JsonSerializerOptions EventJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILogger<AutomationService> _logger;

    // Some services that report events are dependencies of these services. Taking these through the constructor
    // would make those reporters depend on themselves. They are acquired on first use instead.
    private IUserInterfaceService? _userInterfaceService;
    private INativeControlReader? _nativeControlReader;
    private IWebViewToolBridge? _toolBridge;

    private IUserInterfaceService UserInterfaceService =>
        _userInterfaceService ??= ServiceLocator.AcquireService<IUserInterfaceService>();

    private INativeControlReader NativeControlReader =>
        _nativeControlReader ??= ServiceLocator.AcquireService<INativeControlReader>();

    private IWebViewToolBridge ToolBridge =>
        _toolBridge ??= ServiceLocator.AcquireService<IWebViewToolBridge>();

    public AutomationService(ILogger<AutomationService> logger)
    {
        _logger = logger;
    }

    public void LogEvent(AutomationEvent automationEvent)
    {
        _logger.LogDebug("{AutomationEvent}", FormatEvent(automationEvent));
    }

    /// <summary>
    /// Formats an event as its log line: the record's name, followed by its properties as a JSON object.
    /// </summary>
    internal static string FormatEvent(AutomationEvent automationEvent)
    {
        var eventType = automationEvent.GetType();
        var fields = JsonSerializer.Serialize(automationEvent, eventType, EventJsonOptions);
        return $"{eventType.Name} {fields}";
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
        var locateResult = await ToolBridge.LocateAsync(resource, options);
        if (locateResult.IsFailure)
        {
            return Result.Fail(locateResult);
        }
        var locateJson = locateResult.Value;

        return await RunOnUIThreadAsync(() => PlacePageElements(resource, locateJson));
    }

    private Result<ControlSnapshot> GetControls()
    {
        if (UserInterfaceService.XamlRoot is not XamlRoot xamlRoot)
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
        if (UserInterfaceService.XamlRoot is not XamlRoot xamlRoot)
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
        if (UserInterfaceService.XamlRoot is not XamlRoot xamlRoot)
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
        var webViews = VisualTreeReader.ReadControls(xamlRoot, NativeControlReader, element => element is WebView2);
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
        foreach (var showingControl in VisualTreeReader.ReadControls(xamlRoot, NativeControlReader))
        {
            yield return showingControl;
        }

        foreach (var showingControl in NativeControlReader.ReadControls())
        {
            yield return showingControl;
        }
    }

    // Tool requests arrive on a background thread, but only the UI thread can read the visual tree. This uses the
    // dispatcher rather than the command queue, so a call still returns while a modal dialog blocks the queue.
    private Task<Result<T>> RunOnUIThreadAsync<T>(Func<Result<T>> operation) where T : notnull
    {
        if (UserInterfaceService.MainWindow is not Window mainWindow)
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
