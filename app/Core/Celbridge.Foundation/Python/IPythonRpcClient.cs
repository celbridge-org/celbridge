namespace Celbridge.Python;

/// <summary>
/// System information about the Python environment.
/// </summary>
public record SystemInfo(string OS, string PythonVersion, string Platform);

/// <summary>
/// A client service for invoking Python methods via JSON-RPC.
/// Provides strongly-typed methods for C# to call Python functions.
/// </summary>
public interface IPythonRpcClient
{
    /// <summary>
    /// Gets the version of the celbridge Python package.
    /// </summary>
    Task<Result<string>> GetVersionAsync();

    /// <summary>
    /// Get the system information from the Python environment.
    /// </summary>
    Task<Result<SystemInfo>> GetSystemInfoAsync();
}
