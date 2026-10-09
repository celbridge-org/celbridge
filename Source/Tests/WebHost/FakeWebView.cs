using Celbridge.WebHost;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// A web view with no control, for the services that take one. The page's evaluation, reload and capture are
/// settable. Close raises Closing, and the page's messages and focus signals can be raised.
/// </summary>
internal sealed class FakeWebView : IWebView
{
    private EventHandler? _closing;
    private EventHandler? _focusGained;

    public FakeWebView(string resource)
        : this(new ResourceKey(resource))
    {
    }

    public FakeWebView(ResourceKey resource)
    {
        Resource = resource;
    }

    public ResourceKey Resource { get; set; }

    public string AccessibleName => Resource.ResourceName;

    public Func<string, Task<string>> Evaluate { get; init; } = _ => Task.FromResult("null");

    public Func<bool, Task> Reload { get; init; } = _ => Task.CompletedTask;

    public Func<ScreenshotRequest, Task<ScreenshotData>> Capture { get; init; } =
        _ => throw new NotSupportedException();

    public int ClosingSubscriberCount => _closing?.GetInvocationList().Length ?? 0;

    public int FocusGainedSubscriberCount => _focusGained?.GetInvocationList().Length ?? 0;

    public event EventHandler? Closing
    {
        add => _closing += value;
        remove => _closing -= value;
    }

    public event EventHandler<WebNavigationStartingEventArgs>? NavigationStarting
    {
        add { }
        remove { }
    }

    public event EventHandler<WebNavigationCompletedEventArgs>? NavigationCompleted
    {
        add { }
        remove { }
    }

    public event EventHandler<string>? WebMessageReceived;

    public event EventHandler? FocusGained
    {
        add => _focusGained += value;
        remove => _focusGained -= value;
    }

    public event EventHandler? FocusLost;

    public void Close()
    {
        _closing?.Invoke(this, EventArgs.Empty);
    }

    public void PostMessage(string message)
    {
        WebMessageReceived?.Invoke(this, message);
    }

    public void RaiseFocusGained()
    {
        _focusGained?.Invoke(this, EventArgs.Empty);
    }

    public void RaiseFocusLost()
    {
        FocusLost?.Invoke(this, EventArgs.Empty);
    }

    public Task AddDocumentStartScriptAsync(string script) => Task.CompletedTask;

    public Task<string> EvalAsync(string expression) => Evaluate(expression);

    public Task ReloadAsync(bool clearCache) => Reload(clearCache);

    public Task<ScreenshotData> CaptureScreenshotAsync(ScreenshotRequest request) => Capture(request);
}
