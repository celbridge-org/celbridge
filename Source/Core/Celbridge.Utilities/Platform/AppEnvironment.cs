using System.Reflection;
using Path = System.IO.Path;

namespace Celbridge.Utilities.Platform;

/// <summary>
/// Resolves application-environment facts from the packaging model and the build head, keeping the
/// packaged-versus-unpackaged branching in one place. On the packaged Windows head the version and data
/// folders come from the app package. On the Skia heads they come from the entry assembly and the
/// operating system's per-user folders. Bundled assets resolve the same way on every head.
/// </summary>
public sealed class AppEnvironment : IAppEnvironment
{
    private const string ApplicationDataFolderName = "Celbridge";
    private const string WebHostModuleFolderName = "Celbridge.WebHost";

    // The process working folder captured at startup, before any loaded project changes it.
    private static readonly string LaunchWorkingFolder;

    // Capture the launch working folder when this type is first touched during startup (the pre-DI instance
    // created for the log folder), not lazily on first read of LaunchWorkingFolderPath. By then a loaded
    // project may have changed the working folder, or deleted it, which would make Environment.CurrentDirectory
    // throw. The explicit static constructor forces this precise timing (a field initializer alone is
    // beforefieldinit, so the CLR could defer it to first field access). Environment.CurrentDirectory (not the
    // analyzer-gated Directory facade) keeps this dependency-free utility from needing a filesystem carve-out.
    static AppEnvironment()
    {
        LaunchWorkingFolder = Environment.CurrentDirectory;
    }

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

    public string LaunchWorkingFolderPath => LaunchWorkingFolder;

    public string SharedWebAssetsFolderPath => GetBundledAssetPath(WebHostModuleFolderName, "Web");

    public string GetBundledAssetPath(string moduleFolderName, string relativePath)
    {
        // Callers pass forward-slashed relative paths. Normalize to this platform's separator.
        var normalizedRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);

        // Every head lays a library's bundled content out the same way, in the library's module folder
        // beside the app. The packaged head also flattens each library's Assets folder to the package
        // root, but that copy covers Assets alone, so no caller depends on it.
        return Path.Combine(AppContext.BaseDirectory, moduleFolderName, normalizedRelativePath);
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
