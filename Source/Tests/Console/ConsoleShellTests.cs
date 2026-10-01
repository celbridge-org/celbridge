using Celbridge.Console.Helpers;

namespace Celbridge.Tests.Console;

[TestFixture]
public class ConsoleShellTests
{
    [Test]
    public void Resolve_ReturnsPlatformDefaultShell()
    {
        var shell = ConsoleShell.Resolve();

        if (OperatingSystem.IsWindows())
        {
            shell.Executable.Should().Be("powershell.exe");
            shell.Family.Should().Be(ConsoleShellFamily.PowerShell);
        }
        else
        {
            // The default shell resolves to $SHELL or a rooted fallback.
            shell.Executable.Should().NotBeNullOrEmpty();
            shell.Executable.Should().Contain("/");
        }
    }

    [TestCase("/bin/zsh", "/bin/zsh")]
    [TestCase("/opt/homebrew/bin/bash", "/opt/homebrew/bin/bash")]
    public void ResolvePosix_ZshOrBash_IsTheUsersLoginShell(string loginShell, string expected)
    {
        ConsoleShell.ResolvePosix(loginShell).Executable.Should().Be(expected);
    }

    [TestCase("/usr/bin/fish")]
    [TestCase("/bin/tcsh")]
    [TestCase("")]
    [TestCase(null)]
    public void ResolvePosix_AnyOtherShell_IsThePlatformDefault(string? loginShell)
    {
        var expected = OperatingSystem.IsMacOS() ? "/bin/zsh" : "/bin/bash";

        ConsoleShell.ResolvePosix(loginShell).Executable.Should().Be(expected);
    }

    [TestCase("/bin/zsh", ConsoleShellFamily.Posix, true)]
    [TestCase("/usr/local/bin/zsh", ConsoleShellFamily.Posix, true)]
    [TestCase("/bin/bash", ConsoleShellFamily.Posix, false)]
    [TestCase("/usr/bin/fish", ConsoleShellFamily.Posix, false)]
    [TestCase("powershell.exe", ConsoleShellFamily.PowerShell, false)]
    public void IsZsh_ByExecutableName(string executable, ConsoleShellFamily family, bool expected)
    {
        var shell = new ConsoleShell(executable, family);

        shell.IsZsh.Should().Be(expected);
    }
}
