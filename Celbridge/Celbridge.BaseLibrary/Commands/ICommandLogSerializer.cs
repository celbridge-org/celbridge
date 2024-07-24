namespace Celbridge.Commands;

/// <summary>
/// Provides serialization support for the command log.
/// </summary>
public interface ICommandLogSerializer
{
    /// <summary>
    /// Serializes an object to a json string.
    /// If the object is an ExecutedCommandMessage, then special serialization rules are applied.
    /// If ignoreCommandProperties is true, then the properties of the command are not serialized.
    /// </summary>
    string SerializeObject(object? obj, bool ignoreCommandProperties);
}
