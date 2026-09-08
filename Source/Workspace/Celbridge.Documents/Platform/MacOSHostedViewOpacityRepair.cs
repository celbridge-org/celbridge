using Microsoft.UI.Xaml;

namespace Celbridge.Documents.Platform;

/// <summary>
/// Pushes the documents panel's hosted native views their real opacity, so a view that has just been
/// placed in the panel is painted. No-op off macOS.
/// </summary>
// UNO-BUG: Uno sends a hosted native view's opacity to AppKit only while handling an opacity change on
// some element, and never as part of attaching the view. A view attached after the last such change keeps
// the alpha it was last sent - zero, for a document that was in a background tab - so the document is
// laid out and reachable but invisible until an unrelated opacity change happens to repaint it.
internal static class MacOSHostedViewOpacityRepair
{
    // A value the panel is never left at: the first assignment is the opacity change Uno needs to see, the
    // second restores what the panel actually renders at. Both land in one dispatcher turn, so neither the
    // faded panel nor the intermediate value is ever presented.
    private const double NudgeOpacity = 0.99;

    /// <summary>
    /// Changes the panel's opacity and puts it straight back, which is what makes Uno recompute every
    /// hosted view's opacity from the live visual tree and send it to AppKit.
    /// </summary>
    public static void Repair(IDocumentsPanel documentsPanel)
    {
        if (!OperatingSystem.IsMacOS()
            || documentsPanel is not FrameworkElement panel)
        {
            return;
        }

        var opacity = panel.Opacity;
        panel.Opacity = opacity == NudgeOpacity ? 1 : NudgeOpacity;
        panel.Opacity = opacity;
    }
}
