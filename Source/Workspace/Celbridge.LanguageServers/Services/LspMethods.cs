namespace Celbridge.LanguageServers.Services;

/// <summary>
/// The Language Server Protocol's method names. The language client handles or sends each of these methods. A page
/// and a server both send workspace/applyEdit.
/// </summary>
internal static class LspMethods
{
    // The lifecycle of a server
    public const string Initialize = "initialize";
    public const string Initialized = "initialized";
    public const string Shutdown = "shutdown";
    public const string Exit = "exit";

    // Document synchronization
    public const string DidOpen = "textDocument/didOpen";
    public const string DidChange = "textDocument/didChange";
    public const string DidClose = "textDocument/didClose";
    public const string DidSave = "textDocument/didSave";
    public const string WillSave = "textDocument/willSave";
    public const string WillSaveWaitUntil = "textDocument/willSaveWaitUntil";

    // Changes to the settings
    public const string DidChangeConfiguration = "workspace/didChangeConfiguration";

    // Changes to files on disk
    public const string DidChangeWatchedFiles = "workspace/didChangeWatchedFiles";

    // Diagnostics
    public const string Diagnostic = "textDocument/diagnostic";
    public const string PublishDiagnostics = "textDocument/publishDiagnostics";
    public const string DiagnosticRefresh = "workspace/diagnostic/refresh";

    // Requests from a server
    public const string Configuration = "workspace/configuration";
    public const string WorkspaceFolders = "workspace/workspaceFolders";
    public const string RegisterCapability = "client/registerCapability";
    public const string UnregisterCapability = "client/unregisterCapability";
    public const string WorkDoneProgressCreate = "window/workDoneProgress/create";
    public const string ApplyEdit = "workspace/applyEdit";

        // Requests from a page, answered by the language client itself
    public const string ShowDocument = "window/showDocument";

    // Messages from a server
    public const string LogMessage = "window/logMessage";
    public const string ShowMessage = "window/showMessage";

    // Either direction
    public const string CancelRequest = "$/cancelRequest";
}
