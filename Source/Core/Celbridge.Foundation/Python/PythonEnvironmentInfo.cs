namespace Celbridge.Python;

/// <summary>
/// Stores information about the Python environment reported by the Python
/// process at startup. The Python host sends its package list via RPC
/// notification; the handler stores it here for tools to read.
/// </summary>
public static class PythonEnvironmentInfo
{
    /// <summary>
    /// The list of installed Python packages as "name==version" strings.
    /// Empty until the Python process connects and reports its environment.
    /// </summary>
    public static IReadOnlyList<string> InstalledPackages { get; set; } = [];
}
