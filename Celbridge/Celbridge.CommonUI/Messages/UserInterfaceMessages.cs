using Celbridge.CommonUI.Views;

namespace Celbridge.CommonUI.Messages;

/// <summary>
/// The main window has been activated (i.e. received focus).
/// </summary>
public record MainWindowActivated();

/// <summary>
/// The main window has been deactivated (i.e. lost focus).
/// </summary>
public record MainWindowDeactivated();

/// <summary>
/// The Main Page has been loaded.
/// </summary>
public record MainPageLoadedMessage(MainPage mainPage);

/// <summary>
/// The Main Page has been unloaded.
/// </summary>
public record MainPageUnloadedMessage();

/// <summary>
/// The WorkspaceView page has been loaded.
/// </summary>
public record WorkspacePageLoadedMessage(WorkspacePage workspace);

/// <summary>
/// The WorkspaceView page has been unloaded.
/// </summary>
public record WorkspacePageUnloadedMessage();
