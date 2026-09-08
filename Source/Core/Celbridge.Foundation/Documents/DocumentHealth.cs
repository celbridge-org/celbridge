namespace Celbridge.Documents;

/// <summary>
/// What the host knows about a document's hosted page still working. WakeFailures counts the consecutive
/// keep-alive wakes the page has missed, and ProcessFailures counts the times the process rendering it has
/// died. A document with no hosted page is always healthy.
/// </summary>
public record DocumentHealth(int WakeFailures, int ProcessFailures)
{
    public static readonly DocumentHealth Healthy = new(0, 0);

    public bool IsHealthy => WakeFailures == 0 && ProcessFailures == 0;
}
