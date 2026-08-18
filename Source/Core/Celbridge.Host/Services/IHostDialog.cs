using StreamJsonRpc;

namespace Celbridge.Host;

public static class DialogRpcMethods
{
    public const string PickImage = "dialog/pickImage";
    public const string PickFile = "dialog/pickFile";
    public const string Alert = "dialog/alert";
    public const string Toast = "dialog/toast";
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
    /// Shows a workspace toast. Severity is "info", "warning" or "error", and the message is one
    /// line, already localized by the caller. Naming a resource gives the toast an action that opens
    /// it, at the given one-based line and column when they are set. Best effort: returning means the
    /// host took the notification, not that the user saw it.
    /// </summary>
    [JsonRpcMethod(DialogRpcMethods.Toast)]
    Task<ToastResult> ToastAsync(
        string severity,
        string message,
        string? resource = null,
        string? label = null,
        int line = 0,
        int column = 0);
}
