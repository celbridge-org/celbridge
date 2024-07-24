namespace Celbridge.Commands;

/// <summary>
/// Provides serialization support for the command log.
/// </summary>
public interface ICommandLogSerializer
{
    /// <summary>
    /// Serializes an object to a json string.
    /// </summary>
    string SerializeObject(object? obj);

    /// <summary>
    /// Serializes an ExecutedCommandMessage to a json string.
    /// </summary>
    string SerializeExecutedCommand(ExecutedCommandMessage message);
}
