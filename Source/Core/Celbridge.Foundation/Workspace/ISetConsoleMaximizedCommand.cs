using Celbridge.Commands;

namespace Celbridge.Workspace;

/// <summary>
/// Sets the maximized state of the Console panel.
/// When maximized, the Console panel covers the Documents area.
/// </summary>
public interface ISetConsoleMaximizedCommand : IExecutableCommand
{
    /// <summary>
    /// Whether to maximize the console (true) or restore it (false).
    /// </summary>
    bool IsMaximized { get; set; }
}
