using Celbridge.Platform;

namespace Celbridge.Resources.Platform;

public static class PlatformServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
#if WINDOWS
        services.AddSingleton<IFileManagerLauncher, WindowsFileManagerLauncher>();
#else
        services.AddSingleton<IFileManagerLauncher, SkiaFileManagerLauncher>();
#endif
    }
}
