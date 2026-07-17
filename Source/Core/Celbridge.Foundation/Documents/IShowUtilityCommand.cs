using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Documents;

/// <summary>
/// Reveals a utility by its fully-qualified id, optionally moving it to a dock location first. Resolves the
/// whole id space: a built-in id (BuiltInUtilityIds) selects its Utility Panel rail tab, and a custom
/// utility is revealed wherever it lives. This is the single reveal entry point behind the agent tools.
/// </summary>
public interface IShowUtilityCommand : IExecutableCommand
{
    /// <summary>
    /// The id of the utility to reveal: a built-in id (e.g. "celbridge.explorer") or a custom id.
    /// </summary>
    EditorInstanceId UtilityId { get; set; }

    /// <summary>
    /// Optional dock location to move the utility to before revealing it. Null reveals the utility wherever it
    /// currently is without moving it. Ignored for built-in utilities, which are always in the Utility Panel.
    /// </summary>
    DockLocation? Location { get; set; }
}
