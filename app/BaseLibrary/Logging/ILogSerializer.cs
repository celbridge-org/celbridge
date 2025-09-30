namespace Celbridge.Logging;

/// <summary>
/// Provides serialization support for logging.
/// </summary>
public interface ILogSerializer
{
    /// <summary>
    /// Serializes an object to a json string.
    /// If the object is an ExecutedCommandMessage, then special serialization rules are applied.
    /// If ignoreCommandProperties is true, then the properties of the command are not serialized.
    /// </summary>
    string SerializeObject(object? obj, bool ignoreCommandProperties);
}
