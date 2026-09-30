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

    [TestCase("powershell.exe", ConsoleShellFamily.PowerShell)]
    [TestCase("pwsh", ConsoleShellFamily.PowerShell)]
    [TestCase(@"C:\Program Files\PowerShell\7\pwsh.exe", ConsoleShellFamily.PowerShell)]
    [TestCase("cmd.exe", ConsoleShellFamily.Cmd)]
    [TestCase("/bin/zsh", ConsoleShellFamily.Posix)]
    [TestCase("/bin/bash", ConsoleShellFamily.Posix)]
    [TestCase("/usr/bin/fish", ConsoleShellFamily.Posix)]
    public void ClassifyFamily_ByExecutableName(string executable, ConsoleShellFamily expected)
    {
        ConsoleShell.ClassifyFamily(executable).Should().Be(expected);
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

    [TestCase("/bin/zsh", true)]
    [TestCase("/usr/local/bin/zsh", true)]
    [TestCase("/bin/bash", false)]
    [TestCase("/usr/bin/fish", false)]
    [TestCase("powershell.exe", false)]
    public void IsZsh_ByExecutableName(string executable, bool expected)
    {
        var shell = new ConsoleShell(executable, ConsoleShell.ClassifyFamily(executable));

        shell.IsZsh.Should().Be(expected);
    }
}
