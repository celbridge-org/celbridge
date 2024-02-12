namespace Celbridge.BaseLibrary.Logging;

/// <summary>
/// A message was written to the log
/// </summary>
public record WroteToLogMessage(LogMessageType logMessageType, string logMessage);
