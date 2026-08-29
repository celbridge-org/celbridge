using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Documents;

/// <summary>
/// The area a show request moves a utility to. Named carries the area the caller asked for, and
/// OwnDocumentArea asks for whichever document area the utility declares.
/// </summary>
public sealed record ShowUtilityArea
{
    /// <summary>
    /// A request for the utility's own document area.
    /// </summary>
    public static readonly ShowUtilityArea OwnDocumentArea = new();

    /// <summary>
    /// The area the caller named, or null when the utility's own document area was requested.
    /// </summary>
    public WorkspaceArea? NamedArea { get; private init; }

    /// <summary>
    /// A request for the named area.
    /// </summary>
    public static ShowUtilityArea Named(WorkspaceArea area)
    {
        return new ShowUtilityArea
        {
            NamedArea = area
        };
    }
}

/// <summary>
/// Reveals a utility by its fully-qualified id, optionally moving it to a workspace area first.
/// A built-in id selects its Utility Panel rail tab, while a custom utility is revealed wherever it lives.
/// </summary>
public interface IShowUtilityCommand : IExecutableCommand
{
    /// <summary>
    /// The id of the utility to reveal: a built-in id (e.g. "celbridge.explorer") or a custom id.
    /// </summary>
    EditorId UtilityId { get; set; }

    /// <summary>
    /// Optional area to move the utility to before revealing it. Null reveals the utility wherever it
    /// currently is without moving it. Ignored for built-in utilities, which are always in the Utility Panel.
    /// </summary>
    ShowUtilityArea? Area { get; set; }
}
