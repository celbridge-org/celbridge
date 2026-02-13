using System.Reflection;

namespace Celbridge.Utilities;

/// <summary>
/// Provides information about the runtime application environment.
/// </summary>
public class EnvironmentService : IEnvironmentService
{
    /// <summary>
    /// Returns environment information for the runtime application.
    /// </summary>
    public EnvironmentInfo GetEnvironmentInfo()
    {
#if WINDOWS
        var platform = "Windows";
        var packageVersion = Package.Current.Id.Version;
        var appVersion = $"{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}";
#else
        var platform = "SkiaGtk";
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var appVersion = version != null
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "unknown";
#endif

#if DEBUG
        var configuration = "Debug";
#else
        var configuration = "Release";
#endif

        return new EnvironmentInfo(appVersion, platform, configuration);
    }
}
