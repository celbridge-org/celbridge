using StreamJsonRpc;

namespace Celbridge.Host;

/// <summary>
/// RPC service interface for dialog operations.
/// </summary>
public interface IHostDialog
{
    /// <summary>
    /// Opens an image picker dialog and returns the selected path.
    /// </summary>
    [JsonRpcMethod(RpcMethodNames.DialogPickImage)]
    Task<PickImageResult> PickImageAsync(IReadOnlyList<string>? extensions = null);

    /// <summary>
    /// Opens a file picker dialog and returns the selected path.
    /// </summary>
    [JsonRpcMethod(RpcMethodNames.DialogPickFile)]
    Task<PickFileResult> PickFileAsync(IReadOnlyList<string>? extensions = null);

    /// <summary>
    /// Shows an alert dialog to the user.
    /// </summary>
    [JsonRpcMethod(RpcMethodNames.DialogAlert)]
    Task<AlertResult> AlertAsync(string title, string message);
}
