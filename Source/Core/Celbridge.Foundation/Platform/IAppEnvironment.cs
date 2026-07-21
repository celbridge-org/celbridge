namespace Celbridge.Platform;

/// <summary>
/// A snapshot of the running application's version, platform, and build configuration.
/// </summary>
public record EnvironmentInfo(string AppVersion, string Platform, string Configuration);

/// <summary>
/// Provides facts about the running application: a version, platform, and build-configuration snapshot,
/// and the folders where it stores data and temporary files.
/// </summary>
public interface IAppEnvironment
{
    /// <summary>
    /// Returns the application's version, platform label, and build configuration.
    /// </summary>
    EnvironmentInfo GetEnvironmentInfo();

    /// <summary>
    /// The folder where the application stores private data for the current operating-system user.
    /// </summary>
    string LocalApplicationDataFolderPath { get; }

    /// <summary>
    /// A folder for the application's temporary files.
    /// </summary>
    string TemporaryFolderPath { get; }

    /// <summary>
    /// The process working folder captured at application startup, before any loaded project changes it.
    /// </summary>
    string LaunchWorkingFolderPath { get; }

    /// <summary>
    /// The folder of app-bundled web assets shared by every WebView, which the file server publishes
    /// at /assets/.
    /// </summary>
    string SharedWebAssetsFolderPath { get; }

    /// <summary>
    /// Returns the on-disk path to a bundled asset (file or folder) shipped by the given library module,
    /// at the forward-slashed relative path within that module's bundled files. The relative path must
    /// start with the module's Assets folder; other content roots are not addressable this way.
    /// </summary>
    string GetBundledAssetPath(string moduleFolderName, string relativePath);
}
