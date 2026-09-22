namespace Celbridge.WebHost;

/// <summary>
/// Decides whether a navigation of a page may go ahead. isUserInitiated is true when the user started it, as
/// by clicking a link, and false when the page started it by itself. A gate is asked before any request for
/// the navigation is sent, so it must answer at once, and whatever a refusal calls for happens afterwards.
/// </summary>
public delegate bool NavigationGate(Uri destination, bool isUserInitiated);

/// <summary>
/// The registration a head that needs no gate returns, which holds nothing.
/// </summary>
internal sealed class UngatedNavigations : IDisposable
{
    public static UngatedNavigations Instance { get; } = new();

    private UngatedNavigations()
    {
    }

    public void Dispose()
    {
    }
}
