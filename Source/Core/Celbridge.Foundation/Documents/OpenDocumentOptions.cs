namespace Celbridge.Documents;

/// <summary>
/// Options for opening a document in the documents panel.
/// </summary>
public record OpenDocumentOptions(
    DocumentAddress? Address = null,
    bool ForceReload = false,
    string Location = "",
    bool Activate = true,
    EditorInstanceId EditorId = default,
    string? EditorStateJson = null);
