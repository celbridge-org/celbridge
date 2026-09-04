using Celbridge.Console;
using Celbridge.Console.Helpers;

namespace Celbridge.Tests.Console;

[TestFixture]
public class ShellCommandComposerTests
{
    private const char Escape = (char)27;
    private const char Bell = (char)7;

    private static ConsoleStartupInvocation Command(string executable, params string[] arguments)
    {
        return new ConsoleStartupInvocation(executable, arguments);
    }

    [Test]
    public void Compose_EmptyCommand_ReturnsEmpty()
    {
        var composed = ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, ConsoleStartupInvocation.None);

        composed.Line.Should().BeEmpty();
        composed.ScanMarker.Should().BeNull();
    }

    [Test]
    public void Compose_PlainTokens_NeedNoQuoting()
    {
        var command = Command("celbridge-py", "--python", "3.13", "--with", "numpy", "--offline");

        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command).Line
            .Should().Be("celbridge-py --python 3.13 --with numpy --offline");
        ShellCommandComposer.Compose(ConsoleShellFamily.Posix, command).Line
            .Should().Be("celbridge-py --python 3.13 --with numpy --offline");
        ShellCommandComposer.Compose(ConsoleShellFamily.Cmd, command).Line
            .Should().Be("celbridge-py --python 3.13 --with numpy --offline");
    }

    [Test]
    public void Compose_VersionSpecifier_QuotesRedirectionCharacters()
    {
        // An unquoted '>' would redirect in every shell family.
        var command = Command("celbridge-py", "--with", "pandas>=2");

        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command).Line
            .Should().Be("celbridge-py --with 'pandas>=2'");
        ShellCommandComposer.Compose(ConsoleShellFamily.Posix, command).Line
            .Should().Be("celbridge-py --with 'pandas>=2'");
        ShellCommandComposer.Compose(ConsoleShellFamily.Cmd, command).Line
            .Should().Be("celbridge-py --with \"pandas>=2\"");
    }

    [Test]
    public void Compose_PowerShell_QuotedExecutableGetsCallOperator()
    {
        var command = Command(@"C:\Program Files\App\tool.exe", "-x");

        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command).Line
            .Should().Be(@"& 'C:\Program Files\App\tool.exe' -x");
    }

    [Test]
    public void Compose_PowerShell_UnquotedExecutableHasNoCallOperator()
    {
        var command = Command(@"C:\Tools\tool.exe", "-x");

        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command).Line
            .Should().Be(@"C:\Tools\tool.exe -x");
    }

    [Test]
    public void Compose_Posix_EscapesEmbeddedSingleQuote()
    {
        var command = Command("/bin/echo", "it's");

        ShellCommandComposer.Compose(ConsoleShellFamily.Posix, command).Line
            .Should().Be("/bin/echo 'it'\\''s'");
    }

    [Test]
    public void Compose_PowerShell_DoublesEmbeddedSingleQuote()
    {
        var command = Command("echo", "it's");

        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command).Line
            .Should().Be("echo 'it''s'");
    }

    [Test]
    public void Compose_DollarSign_IsQuotedLiterally()
    {
        // Single quotes keep $ literal in PowerShell and POSIX shells alike.
        var command = Command("echo", "$HOME");

        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command).Line
            .Should().Be("echo '$HOME'");
        ShellCommandComposer.Compose(ConsoleShellFamily.Posix, command).Line
            .Should().Be("echo '$HOME'");
    }

    [Test]
    public void Compose_EmptyArgument_IsQuoted()
    {
        var command = Command("tool", "");

        ShellCommandComposer.Compose(ConsoleShellFamily.Posix, command).Line
            .Should().Be("tool ''");
        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command).Line
            .Should().Be("tool \"\"");
    }

    [Test]
    public void Compose_ReadyMarker_ClearsThenEmitsTheMarker()
    {
        var command = Command("celbridge-py");

        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command, "READY-1234").Line
            .Should().Be("Clear-Host; Write-Host -NoNewline ('READY' + '-1234'); celbridge-py");

        // POSIX emits an invisible OSC via printf octal escapes rather than visible text.
        ShellCommandComposer.Compose(ConsoleShellFamily.Posix, command, "READY-1234").Line
            .Should().Be("clear; printf '\\033]7000;READY-1234\\007'; celbridge-py");
    }

    [Test]
    public void Compose_ReadyMarker_PosixScanMarkerIsTheInvisibleOscBytes()
    {
        var composed = ShellCommandComposer.Compose(ConsoleShellFamily.Posix, Command("celbridge-py"), "READY-1234");

        composed.ScanMarker.Should().Be($"{Escape}]7000;READY-1234{Bell}");
    }

    [Test]
    public void Compose_ReadyMarker_ComposedLineNeverContainsTheScanMarker()
    {
        // The shell echoes this line as it reads it, before executing the write. If the echo contained the
        // scan marker the document would reveal the terminal before the clear had run.
        const string marker = "CELBRIDGE-CONSOLE-READY-a1b2c3d4";
        var command = Command("celbridge-py");

        foreach (var family in new[] { ConsoleShellFamily.PowerShell, ConsoleShellFamily.Posix })
        {
            var composed = ShellCommandComposer.Compose(family, command, marker);
            composed.ScanMarker.Should().NotBeNull();
            composed.Line.Should().NotContain(composed.ScanMarker!);
        }
    }

    [Test]
    public void Compose_ReadyMarker_CmdClearsWithoutAMarker()
    {
        var command = Command("celbridge-py");

        ShellCommandComposer.SupportsReadyMarker(ConsoleShellFamily.Cmd).Should().BeFalse();

        var composed = ShellCommandComposer.Compose(ConsoleShellFamily.Cmd, command, "READY-1234");
        composed.Line.Should().Be("cls & celbridge-py");
        composed.ScanMarker.Should().BeNull();
    }

    [Test]
    public void Compose_ReadyMarker_GoesBeforeTheCallOperator()
    {
        var command = Command(@"C:\Program Files\App\tool.exe");

        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command, "READY-1234").Line
            .Should().Be(@"Clear-Host; Write-Host -NoNewline ('READY' + '-1234'); & 'C:\Program Files\App\tool.exe'");
    }

    [Test]
    public void Compose_ReadyMarker_PosixPlainShellRevealsWithoutACommand()
    {
        var composed = ShellCommandComposer.Compose(ConsoleShellFamily.Posix, ConsoleStartupInvocation.None, "READY-1234");

        composed.Line.Should().Be("clear; printf '\\033]7000;READY-1234\\007';");
        composed.ScanMarker.Should().Be($"{Escape}]7000;READY-1234{Bell}");
    }

    [Test]
    public void Compose_ReadyMarker_PowerShellPlainShellStaysEmpty()
    {
        var composed = ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, ConsoleStartupInvocation.None, "READY-1234");

        composed.Line.Should().BeEmpty();
        composed.ScanMarker.Should().BeNull();
    }

    [Test]
    public void Compose_WorkingDirectory_SetsTheLocationBeforeTheCommand()
    {
        var command = Command("celbridge-py");

        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command, workingDirectory: @"C:\Projects\Demo").Line
            .Should().Be(@"Set-Location -LiteralPath 'C:\Projects\Demo'; celbridge-py");
        ShellCommandComposer.Compose(ConsoleShellFamily.Posix, command, workingDirectory: "/home/demo").Line
            .Should().Be("cd '/home/demo'; celbridge-py");
        ShellCommandComposer.Compose(ConsoleShellFamily.Cmd, command, workingDirectory: @"C:\Projects\Demo").Line
            .Should().Be("cd /d \"C:\\Projects\\Demo\" & celbridge-py");
    }

    [Test]
    public void Compose_WorkingDirectory_QuotesAPathWithSpaces()
    {
        var command = Command("celbridge-py");

        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command, workingDirectory: @"C:\My Projects\Demo").Line
            .Should().Be(@"Set-Location -LiteralPath 'C:\My Projects\Demo'; celbridge-py");
        ShellCommandComposer.Compose(ConsoleShellFamily.Posix, command, workingDirectory: "/home/demo user").Line
            .Should().Be("cd '/home/demo user'; celbridge-py");
        ShellCommandComposer.Compose(ConsoleShellFamily.Cmd, command, workingDirectory: @"C:\My Projects\Demo").Line
            .Should().Be("cd /d \"C:\\My Projects\\Demo\" & celbridge-py");
    }

    [Test]
    public void Compose_WorkingDirectory_QuotesAPathCarryingACharacterNeedsQuotingCallsSafe()
    {
        // A comma is safe in a command token but not in a path: unquoted, PowerShell reads it in argument
        // position as an array separator and Set-Location is handed the parts joined by a space.
        var command = Command("celbridge-py");

        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command, workingDirectory: @"C:\Projects,2026\Demo").Line
            .Should().Be(@"Set-Location -LiteralPath 'C:\Projects,2026\Demo'; celbridge-py");
    }

    [Test]
    public void Compose_WorkingDirectory_ComesAfterTheReadyMarkerReveal()
    {
        var command = Command("celbridge-py");

        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command, "READY-1234", @"C:\Projects\Demo").Line
            .Should().Be(@"Clear-Host; Write-Host -NoNewline ('READY' + '-1234'); Set-Location -LiteralPath 'C:\Projects\Demo'; celbridge-py");
    }

    [Test]
    public void Compose_NoWorkingDirectory_InjectsNoLocationChange()
    {
        var command = Command("celbridge-py");

        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command).Line
            .Should().Be("celbridge-py");
        ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, command, workingDirectory: "   ").Line
            .Should().Be("celbridge-py");
    }

    [Test]
    public void Compose_WorkingDirectory_PlainShellIsUnaffected()
    {
        // No command is injected for a plain shell, so there is nothing to navigate before.
        var composed = ShellCommandComposer.Compose(ConsoleShellFamily.PowerShell, ConsoleStartupInvocation.None, workingDirectory: @"C:\Projects\Demo");

        composed.Line.Should().BeEmpty();
    }
}
