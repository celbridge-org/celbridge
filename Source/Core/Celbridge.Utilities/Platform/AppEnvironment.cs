using System.Reflection;
using Celbridge.ApplicationEnvironment;
using Path = System.IO.Path;

namespace Celbridge.Utilities.Platform;

/// <summary>
/// Resolves application-environment facts from the packaging model and the build head, keeping the
/// packaged-versus-unpackaged branching in one place. On the packaged Windows head the version, folders, and
/// bundled assets come from the app package. On the Skia heads they come from the entry assembly and the
/// app's library-layout folders.
/// </summary>
public sealed class AppEnvironment : IAppEnvironment
{
    private const string ApplicationDataFolderName = "Celbridge";

    public EnvironmentInfo GetEnvironmentInfo()
    {
        var appVersion = ResolveAppVersion();
        var platform = ResolvePlatformName();

#if DEBUG
        var configuration = "Debug";
#else
        var configuration = "Release";
#endif

        return new EnvironmentInfo(appVersion, platform, configuration);
    }

    public string LocalApplicationDataFolderPath
    {
        get
        {
#if WINDOWS
            // The packaged app's LocalFolder is already private to Celbridge.
            return Windows.Storage.ApplicationData.Current.LocalFolder.Path;
#else
            // The Skia heads' local data folder is shared between applications, so a Celbridge subfolder
            // keeps Celbridge's data from colliding with other applications.
            var localDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localDataPath, ApplicationDataFolderName);
#endif
        }
    }

    public string TemporaryFolderPath
    {
        get
        {
#if WINDOWS
            return Windows.Storage.ApplicationData.Current.TemporaryFolder.Path;
#else
            return Path.GetTempPath();
#endif
        }
    }

    public string GetBundledAssetPath(string moduleFolderName, string relativePath)
    {
        // Callers pass forward-slashed relative paths. Normalize to this platform's separator.
        var normalizedRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);

#if WINDOWS
        // The packaged head flattens every library's Content assets to the package root, so the module
        // folder is not part of the path. InstalledLocation is a real on-disk folder.
        var root = Windows.ApplicationModel.Package.Current.InstalledLocation.Path;
        return Path.Combine(root, normalizedRelativePath);
#else
        // The Skia heads place each library's assets next to the app under the Uno library layout
        // "<base>/<moduleFolderName>/...".
        return Path.Combine(AppContext.BaseDirectory, moduleFolderName, normalizedRelativePath);
#endif
    }

    private static string ResolveAppVersion()
    {
#if WINDOWS
        var packageVersion = Windows.ApplicationModel.Package.Current.Id.Version;
        return $"{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}";
#else
        // The Uno.Sdk maps ApplicationDisplayVersion onto the app assembly version. GetExecutingAssembly
        // would return this utilities library, whose version is unset. Fall back to the executing assembly
        // if there is no entry assembly.
        var appAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = appAssembly.GetName().Version;
        return version != null
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "unknown";
#endif
    }

    private static string ResolvePlatformName()
    {
#if WINDOWS
        return "Windows";
#else
        return "SkiaGtk";
#endif
    }
}
