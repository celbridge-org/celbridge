using Microsoft.UI.Xaml;

namespace Celbridge.Documents.Platform;

/// <summary>
/// Restores the clip that lets the documents panel's hosted native views show through the Skia canvas.
/// No-op off macOS.
/// </summary>
// UNO-BUG: the Skia canvas covers the native views restored into the panel, so a restored document shows
// nothing until the visual tree changes.
internal static class MacOSHostedViewClipRepair
{
    /// <summary>
    /// Cycles the panel's visibility so the clip is recomputed against arranged geometry. Both states are
    /// applied in one dispatcher turn, so the collapsed panel is never presented.
    /// </summary>
    public static void Repair(IDocumentsPanel documentsPanel)
    {
        if (!OperatingSystem.IsMacOS()
            || documentsPanel is not FrameworkElement panel)
        {
            return;
        }

        panel.Visibility = Visibility.Collapsed;
        panel.UpdateLayout();
        panel.Visibility = Visibility.Visible;
        panel.UpdateLayout();
    }
}
