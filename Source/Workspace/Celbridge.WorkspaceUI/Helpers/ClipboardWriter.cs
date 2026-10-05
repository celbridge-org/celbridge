using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;

namespace Celbridge.WorkspaceUI.Helpers;

/// <summary>
/// Writes data packages to the system clipboard. On Windows, a write fails while another application has the
/// clipboard open, so a failed write is retried for about half a second.
/// </summary>
public static class ClipboardWriter
{
    // CLIPBRD_E_CANT_OPEN: another application has the clipboard open.
    private const int ClipboardInUse = unchecked((int)0x800401D0);
    private const int MaxAttempts = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Sets the clipboard content. If flush is true, the content stays on the clipboard after the app exits.
    /// Returns a failure if the clipboard is still in use after the last retry.
    /// </summary>
    public static async Task<Result> SetContentAsync(DataPackage dataPackage, bool flush = false)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                Clipboard.SetContent(dataPackage);
                if (flush)
                {
                    Clipboard.Flush();
                }
                return Result.Ok();
            }
            // COMException exists on every platform, but only Windows throws this one.
            catch (COMException ex) when (ex.HResult == ClipboardInUse)
            {
                if (attempt == MaxAttempts)
                {
                    return Result.Fail("The clipboard is in use by another application.")
                        .WithException(ex);
                }
            }

            await Task.Delay(RetryDelay);
        }
    }
}
