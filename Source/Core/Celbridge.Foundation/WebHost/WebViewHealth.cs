namespace Celbridge.WebHost;

/// <summary>
/// What the host has observed about whether a web view's page still works. WakeFailures counts the keep-alive
/// wakes the page has missed in a row. ProcessFailures counts the times the process rendering the page has died.
/// </summary>
public record WebViewHealth(int WakeFailures, int ProcessFailures)
{
    public static readonly WebViewHealth Healthy = new(0, 0);

    public bool IsHealthy => WakeFailures == 0 && ProcessFailures == 0;
}
