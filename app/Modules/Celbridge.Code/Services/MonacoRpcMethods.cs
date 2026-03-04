namespace Celbridge.Code.Services;

/// <summary>
/// JSON-RPC method names specific to Monaco editor operations.
/// </summary>
public static class MonacoRpcMethods
{
    // Editor operations (host to client)
    public const string EditorInitialize = "editor/initialize";
    public const string EditorSetLanguage = "editor/setLanguage";
    public const string EditorNavigateToLocation = "editor/navigateToLocation";
}
