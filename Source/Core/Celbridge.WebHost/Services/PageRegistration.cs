namespace Celbridge.WebHost;

/// <summary>
/// What a head has hooked up on one page, where it observes the page in more than one place. Disposing it
/// releases them all, so the surface has a single thing to dispose when the page is torn down.
/// </summary>
internal sealed class PageRegistration : IDisposable
{
    private readonly IDisposable[] _registrations;

    public PageRegistration(params IDisposable[] registrations)
    {
        _registrations = registrations;
    }

    public void Dispose()
    {
        foreach (var registration in _registrations)
        {
            registration.Dispose();
        }
    }
}
