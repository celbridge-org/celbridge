using Celbridge.DataTransfer;

namespace Celbridge.WorkspaceUI.Platform;

/// <summary>
/// Registers the WorkspaceUI services whose implementation is selected per platform.
/// </summary>
public static class PlatformServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        // The file clipboard is platform-specific: macOS writes file URLs to NSPasteboard (the WinRT
        // storage-item clipboard does not round-trip on the Skia head), other heads use the WinRT
        // clipboard. It is a singleton because the macOS implementation remembers the copy/move mode of
        // its own write across calls.
        if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton<IFileClipboard, MacFileClipboard>();
        }
        else
        {
            services.AddSingleton<IFileClipboard, WinRtFileClipboard>();
        }
    }
}
