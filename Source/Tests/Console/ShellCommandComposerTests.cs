using Celbridge.Console;
using Celbridge.Console.Helpers;

namespace Celbridge.Tests.Console;

[TestFixture]
public class ShellCommandComposerTests
{
    private static ConsoleStartupInvocation Command(string executable, params string[] arguments)
    {
        return new ConsoleStartupInvocation(executable, arguments);
    }

    [Test]
    public void Compose_EmptyCommand_ReturnsEmpty()
    {
        var composed = ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, ConsoleStartupInvocation.None);

        composed.Should().BeEmpty();
    }

    [Test]
    public void Compose_PlainTokens_NeedNoQuoting()
    {
        var command = Command("celbridge-py", "--python", "3.13", "--with", "numpy", "--offline");

        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command)
            .Should().Be("celbridge-py --python 3.13 --with numpy --offline");
        ShellCommandComposer.Compose(ConsoleShellFamily.Posix, command)
            .Should().Be("celbridge-py --python 3.13 --with numpy --offline");
        ShellCommandComposer.Compose(ConsoleShellFamily.Cmd, command)
            .Should().Be("celbridge-py --python 3.13 --with numpy --offline");
    }

    [Test]
    public void Compose_VersionSpecifier_QuotesRedirectionCharacters()
    {
        // An unquoted '>' would redirect in every shell family.
        var command = Command("celbridge-py", "--with", "pandas>=2");

        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command)
            .Should().Be("celbridge-py --with 'pandas>=2'");
        ShellCommandComposer.Compose(ConsoleShellFamily.Posix, command)
            .Should().Be("celbridge-py --with 'pandas>=2'");
        ShellCommandComposer.Compose(ConsoleShellFamily.Cmd, command)
            .Should().Be("celbridge-py --with \"pandas>=2\"");
    }

    [Test]
    public void Compose_PowerShell_QuotedExecutableGetsCallOperator()
    {
        var command = Command(@"C:\Program Files\App\tool.exe", "-x");

        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command)
            .Should().Be(@"& 'C:\Program Files\App\tool.exe' -x");
    }

    [Test]
    public void Compose_PowerShell_UnquotedExecutableHasNoCallOperator()
    {
        var command = Command(@"C:\Tools\tool.exe", "-x");

        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command)
            .Should().Be(@"C:\Tools\tool.exe -x");
    }

    [Test]
    public void Compose_Posix_EscapesEmbeddedSingleQuote()
    {
        var command = Command("/bin/echo", "it's");

        ShellCommandComposer.Compose(ConsoleShellFamily.Posix, command)
            .Should().Be("/bin/echo 'it'\\''s'");
    }

    [Test]
    public void Compose_PowerShell_DoublesEmbeddedSingleQuote()
    {
        var command = Command("echo", "it's");

        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command)
            .Should().Be("echo 'it''s'");
    }

    [Test]
    public void Compose_DollarSign_IsQuotedLiterally()
    {
        // Single quotes keep $ literal in PowerShell and POSIX shells alike.
        var command = Command("echo", "$HOME");

        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command)
            .Should().Be("echo '$HOME'");
        ShellCommandComposer.Compose(ConsoleShellFamily.Posix, command)
            .Should().Be("echo '$HOME'");
    }

    [Test]
    public void Compose_EmptyArgument_IsQuoted()
    {
        var command = Command("tool", "");

        ShellCommandComposer.Compose(ConsoleShellFamily.Posix, command)
            .Should().Be("tool ''");
        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command)
            .Should().Be("tool \"\"");
    }

    [Test]
    public void Compose_ReadyMarker_ClearsThenEchoesTheMarker()
    {
        var command = Command("celbridge-py");

        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command, "READY-1234")
            .Should().Be("Clear-Host; Write-Host -NoNewline ('READY' + '-1234'); celbridge-py");
        ShellCommandComposer.Compose(ConsoleShellFamily.Posix, command, "READY-1234")
            .Should().Be("clear; printf '%s' 'READY''-1234'; celbridge-py");
    }

    [Test]
    public void Compose_ReadyMarker_IsSplitSoTheEchoedLineDoesNotContainIt()
    {
        // The shell echoes this line as it reads it, before executing the write. If the echo contained
        // the marker the document would reveal the terminal before the clear had run.
        const string marker = "CELBRIDGE-CONSOLE-READY-a1b2c3d4";
        var command = Command("celbridge-py");

        foreach (var family in new[] { ConsoleShellFamily.PowerShell, ConsoleShellFamily.Posix })
        {
            var line = ShellCommandComposer.Compose(family, command, marker);
            line.Should().NotContain(marker);
        }
    }

    [Test]
    public void Compose_ReadyMarker_CmdClearsWithoutAMarker()
    {
        var command = Command("celbridge-py");

        ShellCommandComposer.SupportsReadyMarker(ConsoleShellFamily.Cmd).Should().BeFalse();
        ShellCommandComposer.Compose(ConsoleShellFamily.Cmd, command, "READY-1234")
            .Should().Be("cls & celbridge-py");
    }

    [Test]
    public void Compose_ReadyMarker_GoesBeforeTheCallOperator()
    {
        var command = Command(@"C:\Program Files\App\tool.exe");

        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command, "READY-1234")
            .Should().Be(@"Clear-Host; Write-Host -NoNewline ('READY' + '-1234'); & 'C:\Program Files\App\tool.exe'");
    }

    [Test]
    public void Compose_ReadyMarker_EmptyCommandStaysEmpty()
    {
        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, ConsoleStartupInvocation.None, "READY-1234")
            .Should().BeEmpty();
    }
}
