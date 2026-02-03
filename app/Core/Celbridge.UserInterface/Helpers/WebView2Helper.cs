using Microsoft.Web.WebView2.Core;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Helper class for centralized WebView2 keyboard shortcut handling.
/// Provides JavaScript injection and message handling for global shortcuts.
/// </summary>
public static class WebView2Helper
{
    /// <summary>
    /// JavaScript that captures global keyboard shortcuts and sends them to the C# host.
    /// Uses capture phase to intercept before other handlers.
    /// </summary>
    public const string KeyboardShortcutScript = @"
        (function() {
            window.addEventListener('keydown', function(event) {
                // Handle F11 for fullscreen toggle
                if (event.key === 'F11') {
                    event.preventDefault();
                    if (window.chrome && window.chrome.webview) {
                        window.chrome.webview.postMessage(JSON.stringify({
                            type: 'keyboard_shortcut',
                            key: 'F11',
                            ctrlKey: event.ctrlKey,
                            shiftKey: event.shiftKey,
                            altKey: event.altKey
                        }));
                    }
                }
            }, true); // Use capture phase to intercept before other handlers
        })();
    ";

    /// <summary>
    /// Injects the keyboard shortcut handler into a WebView2 control.
    /// This should be called after EnsureCoreWebView2Async() completes.
    /// </summary>
    public static async Task InjectKeyboardShortcutHandlerAsync(CoreWebView2 coreWebView2)
    {
        await coreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(KeyboardShortcutScript);
    }

    /// <summary>
    /// Attempts to handle a WebView2 message as a keyboard shortcut.
    /// </summary>
    public static bool HandleKeyboardShortcut(string? message)
    {
        var keyboardShortcutService = ServiceLocator.AcquireService<IKeyboardShortcutService>();
        return keyboardShortcutService.HandleWebView2KeyboardShortcut(message);
    }
}
