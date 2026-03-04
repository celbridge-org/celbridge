using StreamJsonRpc;

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

/// <summary>
/// Extension methods for sending Monaco-specific RPC notifications to the JavaScript client.
/// </summary>
public static class MonacoRpcExtensions
{
    /// <summary>
    /// Initializes the Monaco editor with the specified language.
    /// </summary>
    public static Task NotifyEditorInitializeAsync(this JsonRpc rpc, string language)
    {
        return rpc.NotifyWithParameterObjectAsync(MonacoRpcMethods.EditorInitialize, new { language });
    }

    /// <summary>
    /// Sets the language mode of the Monaco editor.
    /// </summary>
    public static Task NotifyEditorSetLanguageAsync(this JsonRpc rpc, string language)
    {
        return rpc.NotifyWithParameterObjectAsync(MonacoRpcMethods.EditorSetLanguage, new { language });
    }

    /// <summary>
    /// Navigates to a specific location in the Monaco editor.
    /// </summary>
    public static Task NotifyEditorNavigateToLocationAsync(this JsonRpc rpc, int lineNumber, int column)
    {
        return rpc.NotifyWithParameterObjectAsync(MonacoRpcMethods.EditorNavigateToLocation, new { lineNumber, column });
    }
}
