using Celbridge.Console.Services;

namespace Celbridge.Console.Platform;

/// <summary>
/// Creates the pseudo-terminal backend for the current platform: ConPtyTerminal wraps the Windows
/// pseudo-console API, UnixPtyTerminal wraps openpty/posix_spawn on the macOS and Linux heads. Returns
/// null on a platform with no backend, where the terminal reports itself unsupported.
/// </summary>
internal static class PtyBackendFactory
{
    public static IPtyBackend? Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new ConPtyTerminal();
        }

        if (OperatingSystem.IsMacOS()
            || OperatingSystem.IsLinux())
        {
            return new UnixPtyTerminal();
        }

        return null;
    }
}
