using StreamJsonRpc;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// RPC service interface for dialog operations.
/// </summary>
public interface IHostDialog
{
    /// <summary>
    /// Opens an image picker dialog and returns the selected path.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.DialogPickImage)]
    Task<PickImageResult> PickImageAsync(PickImageParams request);

    /// <summary>
    /// Opens a file picker dialog and returns the selected path.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.DialogPickFile)]
    Task<PickFileResult> PickFileAsync(PickFileParams request);

    /// <summary>
    /// Shows an alert dialog to the user.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.DialogAlert)]
    Task<AlertResult> AlertAsync(AlertParams request);
}
