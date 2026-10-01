namespace Celbridge.Console.Helpers;

/// <summary>
/// The quoting and invocation dialect of a console's hosting shell.
/// </summary>
public enum ConsoleShellFamily
{
    PowerShell,
    Posix,
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
    /// Returns the shell a console runs, without searching PATH. Windows uses powershell.exe, which always ships
    /// in System32. macOS and Linux use the user's $SHELL, or a default given by its full path.
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
    /// The shell a console runs on macOS and Linux. This is the user's login shell when it is zsh or bash.
    /// Otherwise it is the platform's default shell, because the start-up files only support zsh and bash.
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
}
