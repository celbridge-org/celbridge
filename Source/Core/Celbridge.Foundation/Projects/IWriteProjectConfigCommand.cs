using Celbridge.Commands;

namespace Celbridge.Projects;

/// <summary>
/// Applies a batch of edits to the current project's .celbridge file. The file is parsed, the edits
/// are applied to the model in order, and it is serialized back in canonical form as one operation,
/// so comments, key ordering, and unknown keys are not preserved (the file is normalized on every
/// load anyway). The running workspace only reflects the edits after a reload.
/// </summary>
public interface IWriteProjectConfigCommand : IExecutableCommand
{
    /// <summary>
    /// The edits to apply to the .celbridge file, in order.
    /// </summary>
    IReadOnlyList<ProjectConfigEdit> Edits { get; set; }
}
