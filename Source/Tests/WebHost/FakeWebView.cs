using Celbridge.WebHost;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// A web view with no control, for the services that take one. The page's evaluation, reload and capture are
/// settable. Close raises Closing.
/// </summary>
internal sealed class FakeWebView : IWebView
{
    private EventHandler? _closing;

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

    public void Close()
    {
        _closing?.Invoke(this, EventArgs.Empty);
    }

    public Task AddDocumentStartScriptAsync(string script) => Task.CompletedTask;

    public Task<string> EvalAsync(string expression) => Evaluate(expression);

    public Task ReloadAsync(bool clearCache) => Reload(clearCache);

    public Task<ScreenshotData> CaptureScreenshotAsync(ScreenshotRequest request) => Capture(request);
}
