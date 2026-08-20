namespace Celbridge.Host;

/// <summary>
/// JSON-RPC method names for the editor channel. The host drives the editor surface: moving the caret to a
/// location, reading the current selection, and inserting text at it.
/// </summary>
public static class EditorRpcMethods
{
    public const string NavigateToLocation = "editor/navigateToLocation";
    public const string GetSelectedText = "editor/getSelectedText";
    public const string InsertText = "editor/insertText";
}
