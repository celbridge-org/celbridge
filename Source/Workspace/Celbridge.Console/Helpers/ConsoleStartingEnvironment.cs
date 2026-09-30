namespace Celbridge.Console.Helpers;

/// <summary>
/// The environment a console starts from, before Celbridge's own variables go on top. With the shell profile
/// on it is everything the application inherited. With it off it is only what a working shell and the network
/// need, so the console is the same however the application was launched.
/// </summary>
public static class ConsoleStartingEnvironment
{
    private const string PathVariableName = "PATH";

    // The system folders a clean console starts with on macOS and Linux. The login shell's system start-up
    // files complete it, from /etc/paths on macOS.
    private const string PosixSystemPath = "/usr/bin:/bin:/usr/sbin:/sbin";

    // What a clean console keeps: the session, the locale, the network, and on Windows the system's own
    // variables. A variable missing from here is missing only from a clean console, and visibly so.
    private static readonly HashSet<string> CleanNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "HOME",
        "USER",
        "LOGNAME",
        "SHELL",
        "TMPDIR",
        "LANG",
        "TZ",
        "SSH_AUTH_SOCK",
        "__CF_USER_TEXT_ENCODING",
        "DISPLAY",
        "WAYLAND_DISPLAY",
        "XAUTHORITY",
        "XDG_RUNTIME_DIR",
        "DBUS_SESSION_BUS_ADDRESS",
        "HTTP_PROXY",
        "HTTPS_PROXY",
        "NO_PROXY",
        "ALL_PROXY",
        "SSL_CERT_FILE",
        "SSL_CERT_DIR",
        "SystemRoot",
        "SystemDrive",
        "windir",
        "ComSpec",
        "PATHEXT",
        "OS",
        "USERPROFILE",
        "USERNAME",
        "USERDOMAIN",
        "COMPUTERNAME",
        "HOMEDRIVE",
        "HOMEPATH",
        "APPDATA",
        "LOCALAPPDATA",
        "ProgramData",
        "ProgramFiles",
        "ProgramFiles(x86)",
        "ProgramW6432",
        "CommonProgramFiles",
        "CommonProgramFiles(x86)",
        "CommonProgramW6432",
        "ALLUSERSPROFILE",
        "PUBLIC",
        "TEMP",
        "TMP",
        "PSModulePath",
        "NUMBER_OF_PROCESSORS",
        "PROCESSOR_ARCHITECTURE",
        "PROCESSOR_IDENTIFIER",
    };

    private static readonly string[] CleanPrefixes =
    {
        "LC_",
    };

    // Names are case-insensitive on Windows, so a console's PATH replaces an inherited Path rather than
    // sitting beside it.
    private static StringComparer NameComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <summary>
    /// The environment the application inherited.
    /// </summary>
    public static Dictionary<string, string> ReadInherited()
    {
        var environment = new Dictionary<string, string>(NameComparer);

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key &&
                entry.Value is string value)
            {
                environment[key] = value;
            }
        }

        return environment;
    }

    /// <summary>
    /// The environment a console starts from, given what the application inherited.
    /// </summary>
    public static Dictionary<string, string> Build(IReadOnlyDictionary<string, string> inherited, bool useShellProfile)
    {
        var environment = new Dictionary<string, string>(NameComparer);

        if (useShellProfile)
        {
            foreach (var pair in inherited)
            {
                environment[pair.Key] = pair.Value;
            }

            return environment;
        }

        foreach (var pair in inherited)
        {
            if (IsKeptWhenClean(pair.Key))
            {
                environment[pair.Key] = pair.Value;
            }
        }

        environment[PathVariableName] = ReadSystemPath();

        return environment;
    }

    /// <summary>
    /// Whether a clean console keeps a variable of this name.
    /// </summary>
    public static bool IsKeptWhenClean(string name)
    {
        if (CleanNames.Contains(name))
        {
            return true;
        }

        return CleanPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));
    }

    // Windows keeps the system's PATH in the registry, apart from the part each user adds. This is a runtime
    // operating-system selector, like the shell a console runs.
    private static string ReadSystemPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return PosixSystemPath;
        }

        var systemPath = Environment.GetEnvironmentVariable(PathVariableName, EnvironmentVariableTarget.Machine);

        return systemPath ?? string.Empty;
    }
}
