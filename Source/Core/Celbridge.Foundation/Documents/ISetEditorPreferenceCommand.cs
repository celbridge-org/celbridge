using Celbridge.Commands;

namespace Celbridge.Documents;

/// <summary>
/// Stores the user's preferred document editor for a file extension.
/// </summary>
public interface ISetEditorPreferenceCommand : IExecutableCommand
{
    /// <summary>
    /// The lowercase file extension including the leading dot (e.g. ".md").
    /// </summary>
    string Extension { get; set; }

    /// <summary>
    /// The editor to prefer for this extension. Pass DocumentEditorId.Empty to clear.
    /// </summary>
    DocumentEditorId EditorId { get; set; }
}
