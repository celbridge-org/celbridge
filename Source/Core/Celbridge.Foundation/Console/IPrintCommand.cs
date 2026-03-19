using Celbridge.Commands;

namespace Celbridge.Console;

/// <summary>
/// Prints a message to the log.
/// </summary>
public interface IPrintCommand : IExecutableCommand
{
    /// <summary>
    /// Message to print in the log.
    /// </summary>
    public string Message { get; set; }

    /// <summary>
    /// Message type to print.
    /// </summary>
    public MessageType MessageType { get; set; }
}
