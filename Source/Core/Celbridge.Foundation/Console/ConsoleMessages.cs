namespace Celbridge.Console;

/// <summary>
/// Broadcast whenever an open console changes state.
/// </summary>
public record ConsoleSessionStateChangedMessage(Guid SessionId, ConsoleSessionState State);
