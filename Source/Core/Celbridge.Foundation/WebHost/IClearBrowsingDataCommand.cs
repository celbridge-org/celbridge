using Celbridge.Commands;

namespace Celbridge.WebHost;

/// <summary>
/// Clears the cookies, cached credentials, site data and cache of every WebView in the application. The
/// WebViews share a single profile, so the clear is application-wide and cannot be scoped to a document.
/// </summary>
public interface IClearBrowsingDataCommand : IExecutableCommand
{
}
