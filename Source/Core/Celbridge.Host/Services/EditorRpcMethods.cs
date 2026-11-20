namespace Celbridge.Host;

/// <summary>
/// JSON-RPC method names for the editor channel. The host drives the editor surface: moving the caret to a
/// location, reading the current selection, inserting text at it, and applying edits to the text.
/// </summary>
public static class EditorRpcMethods
{
    public const string NavigateToLocation = "editor/navigateToLocation";
    public const string GetSelectedText = "editor/getSelectedText";
    public const string InsertText = "editor/insertText";
    public const string ApplyEdits = "editor/applyEdits";
}
