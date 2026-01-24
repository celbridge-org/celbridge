namespace Celbridge.Logging;

/// <summary>
/// Message sent when a log event has been recorded.
/// </summary>
public record LogEventMessage(string LogEventJson);
