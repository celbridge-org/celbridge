using Celbridge.Commands;

namespace Celbridge.Workspace;

/// <summary>
/// Routes a standard editing verb to the surface that currently holds focus (IFocusService.EditTarget).
/// </summary>
public interface IPerformEditCommand : IExecutableCommand
{
    /// <summary>
    /// The editing verb to perform on the active target.
    /// </summary>
    EditIntent Intent { get; set; }
}
