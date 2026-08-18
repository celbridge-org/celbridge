using StreamJsonRpc;

namespace Celbridge.Host;

public static class DialogRpcMethods
{
    public const string PickImage = "dialog/pickImage";
    public const string PickFile = "dialog/pickFile";
    public const string Alert = "dialog/alert";
    public const string Notify = "dialog/notify";
}

/// <summary>
/// RPC service interface for dialog operations.
/// </summary>
public interface IHostDialog
{
    /// <summary>
    /// Opens an image picker dialog and returns the selected path.
    /// </summary>
    [JsonRpcMethod(DialogRpcMethods.PickImage)]
    Task<PickImageResult> PickImageAsync(IReadOnlyList<string>? extensions = null);

    /// <summary>
    /// Opens a file picker dialog and returns the selected path.
    /// </summary>
    [JsonRpcMethod(DialogRpcMethods.PickFile)]
    Task<PickFileResult> PickFileAsync(IReadOnlyList<string>? extensions = null);

    /// <summary>
    /// Shows an alert dialog to the user.
    /// </summary>
    [JsonRpcMethod(DialogRpcMethods.Alert)]
    Task<AlertResult> AlertAsync(string title, string message);

    /// <summary>
    /// Raises a workspace notification. Severity is "info", "warning" or "error"; the message is one
    /// line, already localized by the caller. Best effort: returning means the host took the
    /// notification, not that the user saw it.
    /// </summary>
    [JsonRpcMethod(DialogRpcMethods.Notify)]
    Task<NotifyResult> NotifyAsync(string severity, string message);
}
