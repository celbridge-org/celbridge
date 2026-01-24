using Celbridge.Commands;

namespace Celbridge.UserInterface;

/// <summary>
/// A command that performs an application layout transition.
/// </summary>
public interface ISetLayoutCommand : IExecutableCommand
{
    /// <summary>
    /// The layout transition to perform.
    /// </summary>
    LayoutTransition Transition { get; set; }
}
