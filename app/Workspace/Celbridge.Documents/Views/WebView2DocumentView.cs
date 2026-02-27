using Celbridge.Messaging;
using Celbridge.UserInterface.Helpers;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Documents.Views;

/// <summary>
/// Base class for document views that use a WebView2 control.
/// Provides common initialization, keyboard shortcut handling, and cleanup patterns.
/// </summary>
public abstract partial class WebView2DocumentView : DocumentView
{
    private readonly IMessengerService _messengerService;

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
    /// Initializes the WebView2 control with common settings.
    /// Call this after assigning the WebView property or after EnsureCoreWebView2Async().
    /// </summary>
    protected async Task InitializeWebViewAsync()
    {
        if (WebView is null)
        {
            return;
        }

        await WebView.EnsureCoreWebView2Async();

        // Inject centralized keyboard shortcut handler for F11 and other global shortcuts
        await WebView2Helper.InjectKeyboardShortcutHandlerAsync(WebView.CoreWebView2);

        // Subscribe to events
        WebView.GotFocus -= WebView_GotFocus;
        WebView.GotFocus += WebView_GotFocus;

        WebView.WebMessageReceived -= WebView_WebMessageReceived;
        WebView.WebMessageReceived += WebView_WebMessageReceived;
    }

    /// <summary>
    /// Called when a web message is received from the WebView.
    /// Override to handle custom messages. Call base implementation for keyboard shortcut handling.
    /// </summary>
    protected virtual void OnWebMessageReceived(string? message)
    {
        // Handle keyboard shortcuts via centralized helper
        WebView2Helper.HandleKeyboardShortcut(message);
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
        if (WebView is not null)
        {
            WebView.GotFocus -= WebView_GotFocus;
            WebView.WebMessageReceived -= WebView_WebMessageReceived;
            WebView.Close();
            WebView = null;
        }

        await base.PrepareToClose();
    }

    private void WebView_GotFocus(object sender, RoutedEventArgs e)
    {
        OnWebViewGotFocus();
    }

    private void WebView_WebMessageReceived(WebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        var message = args.TryGetWebMessageAsString();
        OnWebMessageReceived(message);
    }
}
