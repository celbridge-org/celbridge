using Celbridge.Host;
using Celbridge.UserInterface;

namespace Celbridge.Code.Views;

/// <summary>
/// Handles IHostInput RPC methods for the code editor.
/// Forwards keyboard shortcuts and scroll position changes to the host application.
/// </summary>
internal sealed class CodeEditorInputHandler(
    Action<double> onScrollPositionChanged) : IHostInput
{
    public void OnKeyboardShortcut(string key, bool ctrlKey, bool shiftKey, bool altKey)
    {
        var keyboardShortcutService = ServiceLocator.AcquireService<IKeyboardShortcutService>();
        keyboardShortcutService.HandleShortcut(key, ctrlKey, shiftKey, altKey);
    }

    public void OnScrollPositionChanged(double scrollPercentage)
    {
        onScrollPositionChanged(scrollPercentage);
    }
}
