namespace Celbridge.Console;

/// <summary>
/// A message sent when the Run menu's targets may have changed. A console launched, a console's client connected,
/// a console lost the client's connection, a console session ended, or a console file was renamed.
/// </summary>
public record ConsoleRunTargetsChangedMessage();
