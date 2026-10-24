using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.Workspace;

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
/// The workspace has loaded. ProjectFile is the path of the project file, and ElapsedMs is how long the load
/// took, in milliseconds.
/// </summary>
public record WorkspaceLoadedEvent(string ProjectFile, long ElapsedMs) : AutomationEvent;

/// <summary>
/// The resource monitor has delivered a batch of changes in a root to the root's subscriptions. Keys is how many
/// touched keys the batch held, and ElapsedMs is how long matching them and checking the disk took, in milliseconds.
/// </summary>
public record ResourceChangesDeliveredEvent(string RootName, int Keys, long ElapsedMs) : AutomationEvent;

/// <summary>
/// The watcher of a root lost events, so the root's subscriptions are sent a resync.
/// </summary>
public record ResourceEventsLostEvent(string RootName) : AutomationEvent;

/// <summary>
/// The Explorer has listed and checked the folders on screen, and rebuilt the rows. Rows is how many rows the
/// Explorer shows, and ElapsedMs is how long the listings, the checks and the rebuild took, in milliseconds.
/// </summary>
public record ExplorerTreeBuiltEvent(int Rows, long ElapsedMs) : AutomationEvent;

/// <summary>
/// A search of the project's files has finished without being cancelled. Files is how many files the search
/// checked, and ElapsedMs is how long the search took, in milliseconds.
/// </summary>
public record SearchCompletedEvent(int Files, long ElapsedMs) : AutomationEvent;

/// <summary>
/// A project load failed, so the project did not open. It is reported after any alert the failure raised has
/// been answered. ProjectFile is the path of the project file.
/// </summary>
public record ProjectLoadFailedEvent(string ProjectFile) : AutomationEvent;

/// <summary>
/// A launch skipped its previous project, because another instance has the project open. The launch shows Home
/// instead. ProjectFile is the path of the project file.
/// </summary>
public record PreviousProjectSkippedEvent(string ProjectFile) : AutomationEvent;

/// <summary>
/// An alert is requested, before it is on screen. Title and Message are the alert's text.
/// </summary>
public record AlertShownEvent(string Title, string Message) : AutomationEvent;

/// <summary>
/// A dialog has opened and holds the keyboard. Type is the name of the dialog's class.
/// </summary>
public record DialogOpenedEvent(string Type) : AutomationEvent;

/// <summary>
/// An edit verb was performed on the surface that holds focus. Intent is the verb.
/// </summary>
public record EditCommandPerformedEvent(EditIntent Intent) : AutomationEvent;

/// <summary>
/// An editor's selection could not be copied to the clipboard.
/// </summary>
public record ClipboardCopyFailedEvent : AutomationEvent;

/// <summary>
/// A theme has been applied. Theme is the theme the application now shows, and Setting is the theme setting it
/// was applied from.
/// </summary>
public record ThemeAppliedEvent(UserInterfaceTheme Theme, ApplicationColorTheme Setting) : AutomationEvent;

/// <summary>
/// A web surface took the keyboard, through a grant or a click. Surface is the surface's name.
/// </summary>
public record WebSurfaceFocusedEvent(string Surface) : AutomationEvent;

/// <summary>
/// A web view's navigation failed. Resource is the resource the view shows, Url is where the navigation was
/// heading, and Status is the platform's error status.
/// </summary>
public record WebViewNavigationFailedEvent(string Resource, string Url, string Status) : AutomationEvent;

/// <summary>
/// The process behind a web view's page failed. Resource is the resource the view shows, and Detail is the
/// platform's account of the failure.
/// </summary>
public record WebViewProcessFailedEvent(string Resource, string Detail) : AutomationEvent;

/// <summary>
/// The Python install found the support files current, so it installed nothing.
/// </summary>
public record PythonSupportFilesCurrentEvent : AutomationEvent;

/// <summary>
/// The Python install started republishing the celbridge tool, because the support files are current but the
/// tool is missing.
/// </summary>
public record PythonToolRepublishingEvent : AutomationEvent;

/// <summary>
/// The celbridge tool finished installing. DurationMs is how long the install took, in milliseconds.
/// </summary>
public record PythonToolInstalledEvent(long DurationMs) : AutomationEvent;

/// <summary>
/// A full reinstall of the Python support files started.
/// </summary>
public record PythonReinstallStartedEvent : AutomationEvent;

/// <summary>
/// A full reinstall of the Python support files completed.
/// </summary>
public record PythonReinstallCompletedEvent : AutomationEvent;

/// <summary>
/// The Python install started waiting for another instance of the application to finish its install.
/// </summary>
public record PythonInstallWaitingEvent : AutomationEvent;

/// <summary>
/// The Python install stopped waiting for another instance of the application. DurationMs is how long it waited,
/// in milliseconds.
/// </summary>
public record PythonInstallWaitedEvent(long DurationMs) : AutomationEvent;

/// <summary>
/// Start-up removed the temporary environments that earlier sessions left in the uv cache. Removed is how many it
/// removed, and Total is how many it found.
/// </summary>
public record TemporaryEnvironmentsRemovedEvent(int Removed, int Total) : AutomationEvent;

/// <summary>
/// Start-up left the temporary environments in the uv cache, because another instance of the application is
/// running.
/// </summary>
public record TemporaryEnvironmentsKeptEvent : AutomationEvent;
