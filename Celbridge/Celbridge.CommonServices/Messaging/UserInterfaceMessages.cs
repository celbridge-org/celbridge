using Celbridge.CommonServices.UserInterface;

namespace Celbridge.CommonServices.Messaging;

/// <summary>
/// The main window has been activated (i.e. received focus).
/// </summary>
public record MainWindowActivated();

/// <summary>
/// The main window has been deactivated (i.e. lost focus).
/// </summary>
public record MainWindowDeactivated();

/// <summary>
/// The UI element that provides navigation support has loaded.
/// </summary>
public record NavigationProviderLoadedMessage(INavigationProvider NavigationProvider);

/// <summary>
/// A page has been loaded.
/// The Main Page displays content pages via its Frame.
/// </summary>
public record PageLoadedMessage(Page Page);

/// <summary>
/// A page has been unloaded.
/// The Main Page displays content pages via its Frame.
/// </summary>
public record PageUnloadedMessage(Page Page);

/// <summary>
/// Request navigation to a named page.
/// </summary>
public record RequestPageNavigation(string pageName);