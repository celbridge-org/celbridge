namespace Celbridge.WebHost;

/// <summary>
/// What a head has hooked up on one page: its navigation gate, its commit observer, or several of them
/// where the head decides in more than one place. Disposing it releases every one, so the surface holding
/// the page has a single thing to let go of when the page is torn down.
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
