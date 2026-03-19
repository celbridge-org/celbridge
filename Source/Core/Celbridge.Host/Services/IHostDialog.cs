using StreamJsonRpc;

namespace Celbridge.Host;

public static class DialogRpcMethods
{
    public const string PickImage = "dialog/pickImage";
    public const string PickFile = "dialog/pickFile";
    public const string Alert = "dialog/alert";
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
}
