namespace Celbridge.Console.Helpers;

/// <summary>
/// Builds the environment a console starts from, before Celbridge adds its own variables. With the shell profile
/// on, this is everything Celbridge inherited. With it off, it is only what the shell and the network need.
/// A clean console is then the same however Celbridge was launched.
/// </summary>
public static class ConsoleStartingEnvironment
{
    private const string PathVariableName = "PATH";

    // The system folders a clean console starts with on macOS and Linux. The login shell's system start-up files
    // add the rest. On macOS they read /etc/paths.
    private const string PosixSystemPath = "/usr/bin:/bin:/usr/sbin:/sbin";

    // The variables a clean console keeps: session, locale and network variables, and the Windows system
    // variables. Leaving a variable off this list only affects clean consoles.
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

    // Variable names are case-insensitive on Windows. A console's PATH then replaces an inherited Path, instead
    // of sitting beside it.
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
    /// Returns the environment a console starts from, given the inherited environment.
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

    // Windows stores the system PATH in the registry, separate from each user's additions. The operating system
    // is checked at runtime, as it is when choosing the console's shell.
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
