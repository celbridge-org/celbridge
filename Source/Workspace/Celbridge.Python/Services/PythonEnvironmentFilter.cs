namespace Celbridge.Python.Services;

/// <summary>
/// Removes the uv and Python settings Celbridge did not choose from the processes it starts. It matches every
/// variable that controls uv or the interpreter, except uv's index and network settings. The console start-up
/// files are generated from these lists, and apply the same rule after the user's profile runs.
/// </summary>
public static class PythonEnvironmentFilter
{
    // uv reads its settings only from its own variables and its configuration files. It also picks an interpreter
    // from an active virtual or conda environment. Python's own start-up variables all begin with PYTHON. The
    // prefixes also match variables that later uv releases add. uv run sets UV to its own path for the programs
    // it starts.
    public static IReadOnlyList<string> RemovedPrefixes { get; } = new[]
    {
        "UV_",
        "PYTHON",
    };

    public static IReadOnlyList<string> RemovedNames { get; } = new[]
    {
        "UV",
        "VIRTUAL_ENV",
        "CONDA_PREFIX",
    };

    // uv's index and network settings. A user behind a mirror or a proxy needs them, and they cannot change the
    // interpreter or any folder.
    public static IReadOnlyList<string> KeptPrefixes { get; } = new[]
    {
        "UV_INDEX",
        "UV_HTTP_",
    };

    public static IReadOnlyList<string> KeptNames { get; } = new[]
    {
        "UV_DEFAULT_INDEX",
        "UV_EXTRA_INDEX_URL",
        "UV_NATIVE_TLS",
        "UV_INSECURE_HOST",
        "UV_KEYRING_PROVIDER",
        "UV_PYTHON_INSTALL_MIRROR",
    };

    /// <summary>
    /// The commands Celbridge provides in every console. A profile's alias or function with one of these names
    /// is removed.
    /// </summary>
    public static IReadOnlyList<string> CommandNames { get; } = new[]
    {
        "uv",
        "uvx",
        "celbridge-py",
    };

    // Variable names are case-insensitive only on Windows.
    private static StringComparison NameComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>
    /// Whether the filter removes a variable of this name.
    /// </summary>
    public static bool Removes(string name)
    {
        var comparison = NameComparison;

        var isKept = KeptNames.Any(keptName => string.Equals(name, keptName, comparison)) ||
            KeptPrefixes.Any(prefix => name.StartsWith(prefix, comparison));
        if (isKept)
        {
            return false;
        }

        return RemovedNames.Any(removedName => string.Equals(name, removedName, comparison)) ||
            RemovedPrefixes.Any(prefix => name.StartsWith(prefix, comparison));
    }

    /// <summary>
    /// Removes every variable the filter matches from an environment, and returns the names it removed.
    /// </summary>
    public static IReadOnlyList<string> Apply<TValue>(IDictionary<string, TValue> environment)
    {
        var removedNames = environment.Keys
            .Where(Removes)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        foreach (var name in removedNames)
        {
            environment.Remove(name);
        }

        return removedNames;
    }
}
