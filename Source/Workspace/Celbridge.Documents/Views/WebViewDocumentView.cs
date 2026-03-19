using System.Diagnostics.CodeAnalysis;
using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.Host;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.WebView;
using Celbridge.WebView.Services;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Documents.Views;

/// <summary>
/// Base class for document views that use a WebView2 control.
/// </summary>
public abstract partial class WebViewDocumentView : DocumentView, IHostInput
{
    private readonly IMessengerService _messengerService;
    private readonly IWebViewFactory _webViewFactory;

    // JSON-RPC infrastructure
    private WebViewHostChannel? _hostChannel;

    // Theme syncing state
    private IUserInterfaceService? _userInterfaceService;

    /// <summary>
    /// The Celbridge host for JSON-RPC communication with the WebView.
    /// Subclasses can use this to register additional RPC targets and send notifications.
    /// </summary>
    protected CelbridgeHost? Host { get; private set; }

    // Save tracking state for async save coordination with WebView
    private bool _isSaveInProgress;
    private bool _hasPendingSave;

    /// <summary>
    /// The WebView2 control acquired from the factory.
    /// </summary>
    protected WebView2? WebView { get; private set; }

    /// <summary>
    /// The container panel where the WebView will be placed.
    /// Subclasses must set this in their constructor before calling AcquireWebViewAsync().
    /// </summary>
    protected Panel? WebViewContainer { get; set; }

    protected WebViewDocumentView(
        IMessengerService messengerService,
        IWebViewFactory webViewFactory)
    {
        _messengerService = messengerService;
        _webViewFactory = webViewFactory;
    }

    /// <summary>
    /// Acquires a WebView from the factory and adds it to the WebViewContainer.
    /// Call this once during document view initialization, typically in a Loaded event handler.
    /// </summary>
    [MemberNotNull(nameof(WebView))]
    protected async Task AcquireWebViewAsync()
    {
        if (WebViewContainer is null)
        {
            throw new InvalidOperationException("WebViewContainer must be set before calling AcquireWebViewAsync().");
        }

        if (WebView is not null)
        {
            throw new InvalidOperationException("AcquireWebViewAsync() has already been called. ");
        }

        // Acquire a pre-configured WebView from the factory
#pragma warning disable CS8774 // Member must have a non-null value when exiting
        WebView = await _webViewFactory.AcquireAsync();
#pragma warning restore CS8774

        // Add to the visual tree
        WebViewContainer.Children.Add(WebView);

        // Set up focus handling
        WebView.GotFocus -= WebView_GotFocus;
        WebView.GotFocus += WebView_GotFocus;
    }

    /// <summary>
    /// Initializes the host channel for WebView communication.
    /// Call this after AcquireWebViewAsync() and any view-specific WebView setup.
    /// This registers the base class as a handler for IHostInput (keyboard shortcuts, etc.).
    /// Subclasses should call this, then register additional RPC targets using the Host property.
    /// </summary>
    protected void InitializeHost()
    {
        if (WebView?.CoreWebView2 is null)
        {
            return;
        }

        _hostChannel = new WebViewHostChannel(WebView.CoreWebView2);
        Host = new CelbridgeHost(_hostChannel);

        // Register this view as the handler for IHostInput
        // This provides keyboard shortcut handling for all WebView-based documents
        Host.AddLocalRpcTarget<IHostInput>(this);
    }

    /// <summary>
    /// Starts the host listener. Call this after registering all RPC targets.
    /// </summary>
    protected void StartHostListener()
    {
        Host?.StartListening();
    }

    /// <summary>
    /// Called when a keyboard shortcut is pressed in the WebView.
    /// Default implementation forwards to the keyboard shortcut service.
    /// </summary>
    public void OnKeyboardShortcut(string key, bool ctrlKey, bool shiftKey, bool altKey)
    {
        var keyboardShortcutService = ServiceLocator.AcquireService<IKeyboardShortcutService>();
        keyboardShortcutService.HandleShortcut(key, ctrlKey, shiftKey, altKey);
    }

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

    /// <summary>
    /// Enables automatic theme syncing between the app and the WebView.
    /// Registers for ThemeChangedMessage and applies theme changes to the WebView.
    /// Call this in the view constructor or initialization.
    /// </summary>
    protected void EnableThemeSyncing(IUserInterfaceService userInterfaceService)
    {
        _userInterfaceService = userInterfaceService;
        _messengerService.Register<ThemeChangedMessage>(this, OnThemeChangedMessage);
    }

    private void OnThemeChangedMessage(object recipient, ThemeChangedMessage message)
    {
        if (WebView?.CoreWebView2 is not null)
        {
            ApplyThemeToWebView();
        }
    }

    /// <summary>
    /// Applies the current application theme to the WebView.
    /// Call this after the WebView is initialized to set the initial theme.
    /// </summary>
    protected void ApplyThemeToWebView()
    {
        if (WebView?.CoreWebView2 is null || _userInterfaceService is null)
        {
            return;
        }

        var theme = _userInterfaceService.UserInterfaceTheme;
        WebView.CoreWebView2.Profile.PreferredColorScheme = theme == UserInterfaceTheme.Dark
            ? CoreWebView2PreferredColorScheme.Dark
            : CoreWebView2PreferredColorScheme.Light;
    }

    /// <summary>
    /// Creates a DocumentMetadata instance from the current document state.
    /// Used by WebView-based editors for JSON-RPC communication.
    /// Includes the current UI locale for JS-side localization loading.
    /// </summary>
    protected DocumentMetadata CreateDocumentMetadata()
    {
        var locale = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return new DocumentMetadata(
            DocumentViewModel.FilePath,
            DocumentViewModel.FileResource.ToString(),
            Path.GetFileName(DocumentViewModel.FilePath),
            locale);
    }

    /// <summary>
    /// Opens a URL in the system's default browser using the command service.
    /// </summary>
    protected static void OpenSystemBrowser(ICommandService commandService, string? uri)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return;
        }

        commandService.Execute<IOpenBrowserCommand>(command =>
        {
            command.URL = uri;
        });
    }

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
        // Unregister theme syncing if enabled
        if (_userInterfaceService is not null)
        {
            _messengerService.Unregister<ThemeChangedMessage>(this);
        }

        if (WebView is not null)
        {
            WebView.GotFocus -= WebView_GotFocus;
            WebView.Close();
            WebView = null;
        }

        // Dispose RPC infrastructure
        Host?.Dispose();
        _hostChannel?.Detach();

        Host = null;
        _hostChannel = null;

        await base.PrepareToClose();
    }

    private void WebView_GotFocus(object sender, RoutedEventArgs e)
    {
        OnWebViewGotFocus();
    }
}
