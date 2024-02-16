using Celbridge.CommonUI.Views;

namespace Celbridge.CommonUI.Messages;

/// <summary>
/// The WorkspaceView page has been loaded.
/// </summary>
public record WorkspaceViewLoadedMessage(WorkspaceView workspace);

/// <summary>
/// The WorkspaceView page has been unloaded.
/// </summary>
public record WorkspaceViewUnloadedMessage();