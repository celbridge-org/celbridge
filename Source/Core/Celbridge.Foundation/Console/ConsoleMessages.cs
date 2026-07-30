namespace Celbridge.Console;

/// <summary>
/// Broadcast whenever an open console changes state.
/// </summary>
public record ConsoleSessionStateChangedMessage(Guid SessionId, ConsoleSessionRunState State);

/// <summary>
/// Broadcast when a client connection is bound to its console via session/handshake.
/// </summary>
public record ConsoleSessionConnectedMessage(Guid SessionId);
