using Celbridge.Commands;

namespace Celbridge.UserInterface;

/// <summary>
/// Open a URL in the system default browser.
/// </summary>
public interface IOpenBrowserCommand : IExecutableCommand
{
    /// <summary>
    /// The URL to open in the system default browser.
    /// </summary>
    string URL{ get; set; }
}
