namespace Celbridge.Settings;

/// <summary>
/// Feature flag names used throughout the application.
/// These names must match the keys in appsettings.json and .celbridge files.
/// </summary>
public static class FeatureFlags
{
    /// <summary>
    /// Console panel with IPython REPL terminal.
    /// </summary>
    public const string ConsolePanel = "console-panel";

    /// <summary>
    /// Note editor for structured note-taking.
    /// </summary>
    public const string NoteEditor = "note-editor";
}
