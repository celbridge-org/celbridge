namespace Celbridge.Automation.Platform;

/// <summary>
/// The native control reader for platforms where the visual tree holds every control the API reports.
/// </summary>
internal class NullNativeControlReader : INativeControlReader
{
    public IReadOnlyList<ShowingControl> ReadControls()
    {
        return Array.Empty<ShowingControl>();
    }

    public NativeView? FindNativeView(FrameworkElement element)
    {
        return null;
    }
}
