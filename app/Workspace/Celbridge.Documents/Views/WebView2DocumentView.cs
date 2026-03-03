using Celbridge.Host;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using StreamJsonRpc;

namespace Celbridge.Documents.Views;

/// <summary>
/// Base class for document views that use a WebView2 control.
/// Provides JSON-RPC communication, keyboard shortcut handling, focus handling, and cleanup patterns.
/// </summary>
public abstract partial class WebView2DocumentView : DocumentView, IHostNotifications
{
    private readonly IMessengerService _messengerService;

    // JSON-RPC infrastructure
    private HostChannel? _hostChannel;
    private HostRpcHandler? _rpcHandler;

    /// <summary>
    /// The JSON-RPC instance for communication with the WebView.
    /// Subclasses can use this to register additional RPC targets (e.g., IHostDocument).
    /// </summary>
    protected JsonRpc? Rpc { get; private set; }

    // Save tracking state for async save coordination with WebView
    private bool _isSaveInProgress;
    private bool _hasPendingSave;

    /// <summary>
    /// The WebView2 control. Subclasses should assign this in their constructor or XAML.
    /// </summary>
    protected WebView2? WebView { get; set; }

    /// <summary>
    /// Gets the file resource key for this document.
    /// Used to send focus messages when the WebView gains focus.
    /// </summary>
    protected abstract ResourceKey FileResource { get; }

    protected WebView2DocumentView(IMessengerService messengerService)
    {
        _messengerService = messengerService;
    }

    /// <summary>
    /// Initializes the JSON-RPC infrastructure for WebView communication.
    /// Call this after EnsureCoreWebView2Async() and any custom WebView setup.
    /// This registers the base class as a handler for IHostNotifications (keyboard shortcuts, etc.).
    /// Subclasses should call this, then register additional RPC targets using the Rpc property.
    /// </summary>
    protected void InitializeJsonRpc()
    {
        if (WebView?.CoreWebView2 is null)
        {
            return;
        }

        // Create the JSON-RPC channel
        _hostChannel = new HostChannel(WebView.CoreWebView2);
        _rpcHandler = new HostRpcHandler(_hostChannel);
        Rpc = new JsonRpc(_rpcHandler);

        // Ensure RPC method handlers run on the UI thread
        Rpc.SynchronizationContext = SynchronizationContext.Current;

        // Register this view as the handler for IHostNotifications
        // This provides keyboard shortcut handling for all WebView-based documents
        Rpc.AddLocalRpcTarget<IHostNotifications>(this, null);
    }

    /// <summary>
    /// Starts the JSON-RPC listener. Call this after registering all RPC targets.
    /// </summary>
    protected void StartJsonRpc()
    {
        Rpc?.StartListening();
    }

    /// <summary>
    /// Initializes focus handling for the WebView2 control.
    /// </summary>
    protected void InitializeFocusHandling()
    {
        if (WebView is null)
        {
            return;
        }

        WebView.GotFocus -= WebView_GotFocus;
        WebView.GotFocus += WebView_GotFocus;
    }

    #region IHostNotifications

    /// <summary>
    /// Called when the document content has changed in the WebView.
    /// Override in subclasses to handle document changes.
    /// </summary>
    public virtual void OnDocumentChanged()
    {
        // Default implementation does nothing
    }

    /// <summary>
    /// Called when a link is clicked in the WebView.
    /// Override in subclasses to handle link clicks.
    /// </summary>
    public virtual void OnLinkClicked(string href)
    {
        // Default implementation does nothing
    }

    /// <summary>
    /// Called when an import operation completes in the WebView.
    /// Override in subclasses to handle import completion.
    /// </summary>
    public virtual void OnImportComplete(bool success, string? error = null)
    {
        // Default implementation does nothing
    }

    /// <summary>
    /// Called when the JavaScript client has finished initializing.
    /// Override in subclasses to handle client ready notification.
    /// </summary>
    public virtual void OnClientReady()
    {
        // Default implementation does nothing
    }

    /// <summary>
    /// Called when a keyboard shortcut is pressed in the WebView.
    /// Default implementation forwards to the keyboard shortcut service.
    /// </summary>
    public virtual void OnKeyboardShortcut(string key, bool ctrlKey, bool shiftKey, bool altKey)
    {
        var keyboardShortcutService = ServiceLocator.AcquireService<IKeyboardShortcutService>();
        keyboardShortcutService.HandleShortcut(key, ctrlKey, shiftKey, altKey);
    }

    #endregion

    #region Save Tracking

    /// <summary>
    /// Returns true if a save operation is currently in progress.
    /// </summary>
    protected bool IsSaveInProgress => _isSaveInProgress;

    /// <summary>
    /// Call at the start of SaveDocument() to check and set save state.
    /// Returns true if OK to proceed with save, false if a save is already in progress.
    /// </summary>
    protected bool TryBeginSave()
    {
        if (_isSaveInProgress)
        {
            _hasPendingSave = true;
            return false;
        }

        _isSaveInProgress = true;
        _hasPendingSave = false;
        return true;
    }

    /// <summary>
    /// Call when save completes. Returns true if there's a pending save that needs processing.
    /// Caller should typically call ViewModel.OnDataChanged() to re-trigger the save cycle.
    /// </summary>
    protected bool CompleteSave()
    {
        _isSaveInProgress = false;

        if (_hasPendingSave)
        {
            _hasPendingSave = false;
            return true;
        }

        return false;
    }

    #endregion

    /// <summary>
    /// Called when the WebView gains focus.
    /// Override to add custom focus handling. Call base implementation to send focus message.
    /// </summary>
    protected virtual void OnWebViewGotFocus()
    {
        // Set this document as the active document when the WebView2 receives focus
        var message = new DocumentViewFocusedMessage(FileResource);
        _messengerService.Send(message);
    }

    public override async Task PrepareToClose()
    {
        if (WebView is not null)
        {
            WebView.GotFocus -= WebView_GotFocus;
            WebView.Close();
            WebView = null;
        }

        // Dispose RPC infrastructure
        Rpc?.Dispose();
        _rpcHandler?.Dispose();
        _hostChannel?.Detach();

        Rpc = null;
        _rpcHandler = null;
        _hostChannel = null;

        await base.PrepareToClose();
    }

    private void WebView_GotFocus(object sender, RoutedEventArgs e)
    {
        OnWebViewGotFocus();
    }
}
