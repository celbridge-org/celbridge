using Celbridge.Logging;
using Celbridge.Platform;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Managed keyboard focus for the window. Focus is given up by moving it onto an inert zero-sized
/// placeholder in the window root, because WinUI has no way to express that no control has focus.
/// </summary>
public class ManagedFocus : IManagedFocus
{
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly IPlatformInfo _platformInfo;
    private readonly ILogger<ManagedFocus> _logger;

    private ContentControl? _placeholder;
    private bool _reportedFocusFailure;

    public ManagedFocus(
        IUserInterfaceService userInterfaceService,
        IPlatformInfo platformInfo,
        ILogger<ManagedFocus> logger)
    {
        _userInterfaceService = userInterfaceService;
        _platformInfo = platformInfo;
        _logger = logger;
    }

    public bool IsPopupHoldingFocus
    {
        get
        {
            var focusedElement = GetFocusedElement();

            return focusedElement is not null
                && FocusTracking.IsPopupHosted(focusedElement);
        }
    }

    public bool TryPerformTextEditing(EditIntent intent)
    {
        if (GetFocusedElement() is not TextBox textBox)
        {
            return false;
        }

        // Not gated on CanUndo or CanRedo: Uno reports both false right after typing on the Skia head, while
        // Undo still reverts the text. A chord with nothing to revert is still the control's to swallow.
        switch (intent)
        {
            case EditIntent.Undo:
                textBox.Undo();
                return true;

            case EditIntent.Redo:
                textBox.Redo();
                return true;

            default:
                return false;
        }
    }

    public bool TryMoveCaret(CaretMotion motion, bool extendSelection)
    {
        if (GetFocusedElement() is not TextBox textBox)
        {
            return false;
        }

        var text = textBox.Text ?? string.Empty;

        // The caret sits at the far end of the selection from the anchor, which is where a shifted chord
        // grows or shrinks the selection from.
        var anchor = Math.Clamp(textBox.SelectionStart, 0, text.Length);
        var caret = Math.Clamp(anchor + textBox.SelectionLength, 0, text.Length);
        var target = ResolveCaretTarget(text, caret, motion);

        if (extendSelection)
        {
            textBox.SelectionStart = Math.Min(anchor, target);
            textBox.SelectionLength = Math.Abs(target - anchor);
        }
        else
        {
            textBox.SelectionStart = target;
            textBox.SelectionLength = 0;
        }

        return true;
    }

    // The offset a motion lands on. The line motions bound to the line holding the caret, so they stop at a
    // line break rather than running to the ends of a multi-line box.
    internal static int ResolveCaretTarget(string text, int caret, CaretMotion motion)
    {
        switch (motion)
        {
            case CaretMotion.DocumentStart:
                return 0;

            case CaretMotion.DocumentEnd:
                return text.Length;

            case CaretMotion.LineStart:
                return caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;

            case CaretMotion.LineEnd:
                var lineBreak = text.IndexOf('\n', caret);
                if (lineBreak < 0)
                {
                    return text.Length;
                }

                // A CRLF break leaves the caret before the carriage return, which is where the line's text
                // actually ends.
                return lineBreak > 0 && text[lineBreak - 1] == '\r' ? lineBreak - 1 : lineBreak;

            default:
                return caret;
        }
    }

    public void Yield()
    {
        // Yielding only means something where a web surface's native focus leaves managed focus behind. On
        // the other heads focusing the web view is itself a managed focus change, so there is nothing to
        // yield and managed focus must stay free to move to the web view.
        if (!_platformInfo.HostedWebViewFocusIsNative)
        {
            return;
        }

        var placeholder = _placeholder ??= CreatePlaceholder();
        if (placeholder is null)
        {
            return;
        }

        // Re-applying managed focus the placeholder already holds makes Uno resign the web surface's
        // native focus again, which the first responder monitor reconciles by yielding again, looping.
        if (ReferenceEquals(GetFocusedElement(), placeholder))
        {
            return;
        }

        // Focus is refused outright unless the placeholder is a tab stop, so it becomes one only for the
        // moment it takes focus: a zero-sized stop left in the tab order would strand a Tab press.
        // Moving managed focus makes Uno resign the native first responder, so the page holding the caret
        // sees a blur here. Logged because that blur is indistinguishable, at the page, from the user
        // clicking away.
        _logger.LogTrace("Yielding managed focus to the placeholder");

        placeholder.IsTabStop = true;
        var focused = placeholder.Focus(FocusState.Programmatic);
        placeholder.IsTabStop = false;

        if (!focused
            && !_reportedFocusFailure)
        {
            _reportedFocusFailure = true;
            _logger.LogWarning("Managed focus could not be yielded, so keys may still reach the previously focused control");
        }
    }

    private UIElement? GetFocusedElement()
    {
        if (_userInterfaceService.MainWindow is not Window mainWindow
            || mainWindow.Content is not UIElement rootContent
            || rootContent.XamlRoot is null)
        {
            return null;
        }

        return Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(rootContent.XamlRoot) as UIElement;
    }

    private ContentControl? CreatePlaceholder()
    {
        if (_userInterfaceService.MainWindow is not Window mainWindow
            || mainWindow.Content is not Panel rootPanel)
        {
            return null;
        }

        var placeholder = new ContentControl
        {
            Width = 0,
            Height = 0,
            IsTabStop = false
        };

        // The placeholder belongs to no panel, so without this the focus tracker would classify it as a move
        // off the workspace panels and clear panel focus. Focus landing here means nothing changed.
        FocusTracking.SetPreservePanelFocus(placeholder, true);

        rootPanel.Children.Add(placeholder);

        return placeholder;
    }
}
