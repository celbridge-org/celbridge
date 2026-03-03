namespace Celbridge.Host;

/// <summary>
/// JSON-RPC method names used for WebView2 communication.
/// </summary>
public static class HostRpcMethods
{
    // Host initialization
    public const string Initialize = "host/initialize";

    // Document operations
    public const string DocumentLoad = "document/load";
    public const string DocumentSave = "document/save";

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

    // Lifecycle notifications
    public const string ClientReady = "client/ready";

    // Localization
    public const string LocalizationUpdated = "localization/updated";
}
