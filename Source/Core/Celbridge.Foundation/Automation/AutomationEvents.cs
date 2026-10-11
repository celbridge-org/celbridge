namespace Celbridge.Automation;

/// <summary>
/// A moment that test automation depends on. Each event is a record derived from this one. The record's name is
/// the event's name, and its properties are the event's fields.
/// </summary>
public abstract record AutomationEvent;

/// <summary>
/// The server is listening. Port is the port it listens on.
/// </summary>
public record ServerStartedEvent(int Port) : AutomationEvent;

/// <summary>
/// The workspace has loaded. ElapsedMs is how long the load took, in milliseconds.
/// </summary>
public record WorkspaceLoadedEvent(long ElapsedMs) : AutomationEvent;

/// <summary>
/// A project load failed, so the project did not open. It is reported after any alert the failure raised has
/// been answered. ProjectFile is the path of the project file.
/// </summary>
public record ProjectLoadFailedEvent(string ProjectFile) : AutomationEvent;

/// <summary>
/// An alert is requested, before it is on screen. Title and Message are the alert's text.
/// </summary>
public record AlertShownEvent(string Title, string Message) : AutomationEvent;

/// <summary>
/// A dialog has opened and holds the keyboard. Type is the name of the dialog's class.
/// </summary>
public record DialogOpenedEvent(string Type) : AutomationEvent;
