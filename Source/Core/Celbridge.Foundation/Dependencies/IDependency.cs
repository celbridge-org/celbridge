namespace Celbridge.Dependencies;

/// <summary>
/// A tool that a feature needs on this machine. Celbridge installs the tool before the feature works. Every project
/// on the machine shares the installed tool, and each release of Celbridge pins the tool's version.
/// </summary>
public interface IDependency
{
    /// <summary>
    /// Returns the id that names the dependency in logs and automation events.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Returns the tool's version, as this release of Celbridge pins it.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Returns the localized name of the dependency, for the user.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Returns the localized title that the progress dialog shows while the dependency installs.
    /// </summary>
    string ProgressTitle { get; }

    /// <summary>
    /// Returns true if the pinned version is installed on this machine.
    /// </summary>
    Task<bool> IsInstalledAsync();

    /// <summary>
    /// Installs the pinned version on this machine.
    /// </summary>
    Task<Result> InstallAsync();
}
