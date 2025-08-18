namespace Celbridge.Python;

/// <summary>
/// A service for interacting with Python via the terminal.
/// </summary>
public interface IPythonService
{
    /// <summary>
    /// Initializes the Python environment.
    /// </summary>
    Task<Result> InitializePython();
}
