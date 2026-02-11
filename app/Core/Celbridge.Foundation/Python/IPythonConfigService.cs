namespace Celbridge.Python;

/// <summary>
/// Provides Python configuration values.
/// </summary>
public interface IPythonConfigService
{
    /// <summary>
    /// Gets the default Python version for new projects.
    /// </summary>
    string DefaultPythonVersion { get; }
}
