namespace Celbridge.UserInterface.CelbridgeHost;

/// <summary>
/// JSON-RPC method names used for WebView2 communication.
/// These constants are shared between the legacy CelbridgeHost and the new StreamJsonRpc interfaces.
/// </summary>
public static class HostRpcMethods
{
    // Host initialization (using "bridge/initialize" for backward compatibility with existing JS clients)
    public const string Initialize = "bridge/initialize";

    // Document operations
    public const string DocumentLoad = "document/load";
    public const string DocumentSave = "document/save";
    public const string DocumentGetMetadata = "document/getMetadata";
    public const string DocumentSaveBinary = "document/saveBinary";
    public const string DocumentLoadBinary = "document/loadBinary";

    // Document notifications
    public const string DocumentChanged = "document/changed";
    public const string DocumentRequestSave = "document/requestSave";
    public const string DocumentExternalChange = "document/externalChange";

    // Dialog operations
    public const string DialogPickImage = "dialog/pickImage";
    public const string DialogPickFile = "dialog/pickFile";
    public const string DialogAlert = "dialog/alert";

    // Link operations
    public const string LinkClicked = "link/clicked";

    // Import operations
    public const string ImportComplete = "import/complete";

    // Localization
    public const string LocalizationUpdated = "localization/updated";
}
