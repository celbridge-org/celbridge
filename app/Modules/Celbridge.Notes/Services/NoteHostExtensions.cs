using Celbridge.Host;

namespace Celbridge.Notes.Services;

/// <summary>
/// JSON-RPC method names for Note editor operations (host to client).
/// </summary>
internal static class NoteRpcMethods
{
    public const string NavigateToHeading = "note/navigateToHeading";
    public const string SetTocVisibility = "note/setTocVisibility";
    public const string Focus = "note/focus";
}

/// <summary>
/// Extension methods for CelbridgeHost that provide Note-specific RPC operations.
/// </summary>
public static class NoteHostExtensions
{
    /// <summary>
    /// Navigates to a specific heading in the Note editor.
    /// </summary>
    public static Task NavigateToHeadingAsync(this CelbridgeHost host, string heading)
    {
        return host.Rpc.NotifyWithParameterObjectAsync(NoteRpcMethods.NavigateToHeading, new { heading });
    }

    /// <summary>
    /// Sets the visibility of the Table of Contents panel.
    /// </summary>
    public static Task SetTocVisibilityAsync(this CelbridgeHost host, bool visible)
    {
        return host.Rpc.NotifyWithParameterObjectAsync(NoteRpcMethods.SetTocVisibility, new { visible });
    }

    /// <summary>
    /// Focuses the Note editor.
    /// </summary>
    public static Task FocusAsync(this CelbridgeHost host)
    {
        return host.Rpc.NotifyAsync(NoteRpcMethods.Focus);
    }
}
