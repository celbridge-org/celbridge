namespace Celbridge.ApplicationEnvironment;

/// <summary>
/// Describes the runtime application environment.
/// </summary>
public record EnvironmentInfo(string AppVersion, string Platform, string Configuration);

/// <summary>
/// Provides information about the runtime application environment.
/// </summary>
public interface IEnvironmentService
{
    /// <summary>
    /// Returns environment information for the runtime application.
    /// </summary>
    EnvironmentInfo GetEnvironmentInfo();
}
