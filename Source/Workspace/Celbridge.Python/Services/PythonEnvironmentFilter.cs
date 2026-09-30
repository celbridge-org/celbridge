namespace Celbridge.Python.Services;

/// <summary>
/// The rule that keeps uv and Python settings Celbridge did not choose out of the processes it starts. It matches
/// every variable that steers uv or the interpreter, except uv's index and network settings.
/// </summary>
public static class PythonEnvironmentFilter
{
    // uv is configured only through its own variables and its configuration files, and chooses an interpreter
    // using an active virtual or conda environment. Python's own start-up variables all begin with PYTHON. The
    // prefixes also catch the variables uv adds in later releases. uv run sets UV to its own executable for the
    // programs it starts.
    private static readonly string[] RemovedPrefixes =
    {
        "UV_",
        "PYTHON",
    };

    private static readonly string[] RemovedNames =
    {
        "UV",
        "VIRTUAL_ENV",
        "CONDA_PREFIX",
    };

    // uv's index and network settings, which a user behind a mirror or a proxy needs, and which cannot move an
    // interpreter or a folder.
    private static readonly string[] KeptPrefixes =
    {
        "UV_INDEX",
        "UV_HTTP_",
    };

    private static readonly string[] KeptNames =
    {
        "UV_DEFAULT_INDEX",
        "UV_EXTRA_INDEX_URL",
        "UV_NATIVE_TLS",
        "UV_INSECURE_HOST",
        "UV_KEYRING_PROVIDER",
        "UV_PYTHON_INSTALL_MIRROR",
    };

    // Environment names are case-insensitive on Windows alone.
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
