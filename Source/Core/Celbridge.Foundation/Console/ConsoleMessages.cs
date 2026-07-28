namespace Celbridge.Console;

/// <summary>
/// Broadcast whenever an open console changes state. Consumers filter by session id or resource.
/// </summary>
public record ConsoleSessionStateChangedMessage(Guid SessionId, ConsoleSessionState State);
