using Celbridge.Commands;

namespace Celbridge.Workspace;

/// <summary>
/// Routes a standard editing verb to the surface that currently holds focus (IFocusService.EditTarget).
/// Keyboard handlers and the native and in-window menus all dispatch this command, so every edit path
/// runs through the command service and resolves to the focused surface uniformly.
/// </summary>
public interface IPerformEditCommand : IExecutableCommand
{
    /// <summary>
    /// The editing verb to perform on the active target.
    /// </summary>
    EditIntent Intent { get; set; }
}
