namespace Celbridge.Code.Views;

/// <summary>
/// Shared mutable state between CodeEditor and its RPC handler classes.
/// Both the control and the document handler read and write these fields
/// as part of the editor lifecycle and RPC protocol.
/// </summary>
internal sealed class CodeEditorState
{
    public string Content { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ResourceKey { get; set; } = string.Empty;
    public string ProjectFolderPath { get; set; } = string.Empty;
    public Func<Task<string>>? ContentLoader { get; set; }
}
