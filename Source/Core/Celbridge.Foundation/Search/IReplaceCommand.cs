using Celbridge.Commands;

namespace Celbridge.Search;

/// <summary>
/// Specifies the scope of a replace operation.
/// </summary>
public enum ReplaceScope
{
    /// <summary>
    /// Replace all matches in the current search results.
    /// </summary>
    All,

    /// <summary>
    /// Replace only the currently selected matches.
    /// </summary>
    Selected
}

/// <summary>
/// Executes a replace operation on the current search results.
/// </summary>
public interface IReplaceCommand : IExecutableCommand
{
    /// <summary>
    /// The scope of the replace operation.
    /// </summary>
    ReplaceScope Scope { get; set; }

    /// <summary>
    /// Whether to show a confirmation dialog before replacing.
    /// If null, defaults to true for All scope, false for Selected scope.
    /// </summary>
    bool? ShowConfirmation { get; set; }
}
