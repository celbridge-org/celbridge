namespace Celbridge.Console.Helpers;

/// <summary>
/// The quoting and invocation dialect of a console's hosting shell.
/// </summary>
public enum ConsoleShellFamily
{
    PowerShell,
    Posix,
    Cmd,
}

/// <summary>
/// The shell that hosts every console session: its executable and its command dialect.
/// </summary>
public sealed record ConsoleShell(string Executable, ConsoleShellFamily Family)
{
    /// <summary>
    /// Whether this shell is zsh. The POSIX family shares a quoting dialect but not a prompt syntax, so
    /// anything written in zsh's own syntax has to name the shell rather than the family.
    /// </summary>
    public bool IsZsh => IsNamed(Executable, "zsh");

    /// <summary>
    /// Whether this shell is bash.
    /// </summary>
    public bool IsBash => IsNamed(Executable, "bash");

    /// <summary>
    /// Resolves the shell a console runs. Each is resolvable without a PATH probe: powershell.exe ships in
    /// System32, and on macOS and Linux the shell is the user's $SHELL or a rooted default.
    /// </summary>
    public static ConsoleShell Resolve()
    {
        if (OperatingSystem.IsWindows())
        {
            return new ConsoleShell("powershell.exe", ConsoleShellFamily.PowerShell);
        }

        return ResolvePosix(Environment.GetEnvironmentVariable("SHELL"));
    }

    /// <summary>
    /// The shell a console runs on macOS and Linux: the user's login shell when it is zsh or bash, and
    /// otherwise the platform's default, since the console's start-up files are written for those two.
    /// </summary>
    public static ConsoleShell ResolvePosix(string? loginShell)
    {
        var isSupported = !string.IsNullOrEmpty(loginShell) &&
            (IsNamed(loginShell, "zsh") || IsNamed(loginShell, "bash"));
        if (isSupported)
        {
            return new ConsoleShell(loginShell!, ConsoleShellFamily.Posix);
        }

        var defaultShell = OperatingSystem.IsMacOS() ? "/bin/zsh" : "/bin/bash";
        return new ConsoleShell(defaultShell, ConsoleShellFamily.Posix);
    }

    private static bool IsNamed(string executable, string name)
    {
        return Path.GetFileNameWithoutExtension(executable).Equals(name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Classifies a shell executable's command dialect by its file name.
    /// </summary>
    public static ConsoleShellFamily ClassifyFamily(string shellExecutable)
    {
        var fileName = Path.GetFileNameWithoutExtension(shellExecutable);
        if (fileName.Contains("powershell", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("pwsh", StringComparison.OrdinalIgnoreCase))
        {
            return ConsoleShellFamily.PowerShell;
        }

        if (fileName.Equals("cmd", StringComparison.OrdinalIgnoreCase))
        {
            return ConsoleShellFamily.Cmd;
        }

        return ConsoleShellFamily.Posix;
    }
}
