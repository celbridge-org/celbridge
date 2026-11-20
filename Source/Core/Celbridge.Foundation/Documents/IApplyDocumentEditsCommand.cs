using Celbridge.Commands;

namespace Celbridge.Documents;

/// <summary>
/// The files changed by a set of document edits, in the edits' order.
/// </summary>
public record DocumentEditsResult(IReadOnlyList<ResourceKey> ChangedFiles);

/// <summary>
/// Applies coordinate-based edits to documents. Each edit's range refers to the document's current text, which for an
/// open document is the text in its editor. An open document changes in its editor, so the change joins the
/// document's undo history. The document then saves the change a second later, like typing. A closed file changes on
/// disk. The command checks that every file can change before changing any.
/// </summary>
public interface IApplyDocumentEditsCommand : IExecutableCommand<DocumentEditsResult>
{
    /// <summary>
    /// The edits to apply, one entry for each file.
    /// </summary>
    List<FileRangeEdit> Edits { get; set; }
}
