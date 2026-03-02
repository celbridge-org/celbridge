using StreamJsonRpc;

namespace Celbridge.UserInterface.CelbridgeHost;

/// <summary>
/// RPC service interface for dialog operations.
/// </summary>
public interface IHostDialog
{
    /// <summary>
    /// Opens an image picker dialog and returns the selected path.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.DialogPickImage)]
    Task<PickImageResult> PickImageAsync(IReadOnlyList<string>? extensions = null);

    /// <summary>
    /// Opens a file picker dialog and returns the selected path.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.DialogPickFile)]
    Task<PickFileResult> PickFileAsync(IReadOnlyList<string>? extensions = null);

    /// <summary>
    /// Shows an alert dialog to the user.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.DialogAlert)]
    Task<AlertResult> AlertAsync(string title, string message);
}
