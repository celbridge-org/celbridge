namespace Celbridge.Dependencies;

/// <summary>
/// Software that must be on this machine for a feature to work. Celbridge installs the software when a project with
/// the feature loads. Every project on the machine shares the installed software, and each release of Celbridge pins
/// the software's version.
/// </summary>
public interface IDependency
{
    /// <summary>
    /// Returns the id that names the dependency in logs and automation events.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Returns the software's pinned version.
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
