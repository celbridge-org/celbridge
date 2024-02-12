namespace Celbridge.BaseLibrary.Logging;

/// <summary>
/// A message was written to the log
/// </summary>
public record WroteLogMessage(LogMessageType logMessageType, string logMessage);
