using StreamJsonRpc;

namespace Celbridge.Host;

/// <summary>
/// Extension methods for sending notifications from C# to JavaScript via StreamJsonRpc.
/// </summary>
public static class HostNotificationExtensions
{
    /// <summary>
    /// Notifies the WebView that the document has been externally modified.
    /// </summary>
    public static Task NotifyExternalChangeAsync(this JsonRpc rpc)
    {
        return rpc.NotifyAsync(HostRpcMethods.DocumentExternalChange);
    }

    /// <summary>
    /// Requests the WebView to save the current document content.
    /// JS should respond by calling document/save.
    /// </summary>
    public static Task NotifyRequestSaveAsync(this JsonRpc rpc)
    {
        return rpc.NotifyAsync(HostRpcMethods.DocumentRequestSave);
    }

    /// <summary>
    /// Notifies the WebView that localization strings have been updated.
    /// </summary>
    public static Task NotifyLocalizationUpdatedAsync(this JsonRpc rpc, Dictionary<string, string> strings)
    {
        var notification = new LocalizationUpdatedNotification(strings);
        return rpc.NotifyAsync(HostRpcMethods.LocalizationUpdated, notification);
    }
}
