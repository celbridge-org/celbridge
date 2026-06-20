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
        // Read the entry (application) assembly. The Uno.Sdk maps ApplicationDisplayVersion onto
        // the app assembly version; GetExecutingAssembly would return this utilities library, whose
        // version is unset. Fall back to the executing assembly if there is no entry assembly.
        var appAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = appAssembly.GetName().Version;
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
