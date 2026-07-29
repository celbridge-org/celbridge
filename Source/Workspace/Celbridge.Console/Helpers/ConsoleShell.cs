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
    /// Resolves the platform default shell. Each is resolvable without a PATH probe: powershell.exe ships
    /// in System32, and the Unix path honours the user's $SHELL.
    /// </summary>
    public static ConsoleShell Resolve()
    {
        if (OperatingSystem.IsWindows())
        {
            return new ConsoleShell("powershell.exe", ConsoleShellFamily.PowerShell);
        }

        var loginShell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(loginShell))
        {
            return new ConsoleShell(loginShell, ClassifyFamily(loginShell));
        }

        if (OperatingSystem.IsMacOS())
        {
            return new ConsoleShell("/bin/zsh", ConsoleShellFamily.Posix);
        }

        return new ConsoleShell("/bin/bash", ConsoleShellFamily.Posix);
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
