namespace Celbridge.Python;

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
}
