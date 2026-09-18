using Celbridge.Console;
using Celbridge.Console.Helpers;

namespace Celbridge.Tests.Console;

[TestFixture]
public class ShellCommandComposerTests
{
    private const char Escape = (char)27;
    private const char Bell = (char)7;

    // The marker each shell family emits, and the reveal line that carries it.
    private const char Sentinel = '\u2404';
    private const string PosixMarkerText = "CELBRIDGE-CONSOLE-READY";
    private const string PowerShellReveal = "Clear-Host; Write-Host -NoNewline ([char]0x2404); ";
    private const string PosixReveal = @"clear; printf '\033]7000;CELBRIDGE-CONSOLE-READY\007'; ";
    private const string CmdReveal = "cls & ";

    // Bash stands in for the POSIX family wherever the expectation is about quoting or the marker, so
    // only the prompt tests carry the zsh prompt default.
    private static readonly ConsoleShell PowerShell = new("powershell.exe", ConsoleShellFamily.PowerShell);
    private static readonly ConsoleShell Bash = new("/bin/bash", ConsoleShellFamily.Posix);
    private static readonly ConsoleShell Zsh = new("/bin/zsh", ConsoleShellFamily.Posix);
    private static readonly ConsoleShell Cmd = new("cmd.exe", ConsoleShellFamily.Cmd);

    private const string ZshPromptDefault =
        @"if [[ $PROMPT == '%n@%m %1~ %# ' ]]; then PROMPT='%F{cyan}%1~%f %# '; fi; ";
    private const string ZshReveal = PosixReveal + ZshPromptDefault;

    private static ConsoleStartupInvocation Command(string executable, params string[] arguments)
    {
        return new ConsoleStartupInvocation(executable, arguments);
    }

    [Test]
    public void Compose_EmptyCommand_ReturnsEmpty()
    {
        var composed = ShellCommandComposer.Compose(PowerShell, ConsoleStartupInvocation.None);

        composed.Line.Should().BeEmpty();
        composed.ScanMarker.Should().BeNull();
    }

    [Test]
    public void Compose_PlainTokens_NeedNoQuoting()
    {
        var command = Command("celbridge-py", "--python", "3.13", "--with", "numpy", "--offline");
        const string expected = "celbridge-py --python 3.13 --with numpy --offline";

        ShellCommandComposer.Compose(PowerShell, command).Line
            .Should().Be(PowerShellReveal + expected);
        ShellCommandComposer.Compose(Bash, command).Line
            .Should().Be(PosixReveal + expected);
        ShellCommandComposer.Compose(Cmd, command).Line
            .Should().Be(CmdReveal + expected);
    }

    [Test]
    public void Compose_VersionSpecifier_QuotesRedirectionCharacters()
    {
        // An unquoted '>' would redirect in every shell family.
        var command = Command("celbridge-py", "--with", "pandas>=2");

        ShellCommandComposer.Compose(PowerShell, command).Line
            .Should().Be(PowerShellReveal + "celbridge-py --with 'pandas>=2'");
        ShellCommandComposer.Compose(Bash, command).Line
            .Should().Be(PosixReveal + "celbridge-py --with 'pandas>=2'");
        ShellCommandComposer.Compose(Cmd, command).Line
            .Should().Be(CmdReveal + @"celbridge-py --with ""pandas>=2""");
    }

    [Test]
    public void Compose_PowerShell_QuotedExecutableGetsCallOperator()
    {
        var command = Command(@"C:\Program Files\App\tool.exe", "-x");

        ShellCommandComposer.Compose(PowerShell, command).Line
            .Should().Be(PowerShellReveal + @"& 'C:\Program Files\App\tool.exe' -x");
    }

    [Test]
    public void Compose_PowerShell_UnquotedExecutableHasNoCallOperator()
    {
        var command = Command(@"C:\Tools\tool.exe", "-x");

        ShellCommandComposer.Compose(PowerShell, command).Line
            .Should().Be(PowerShellReveal + @"C:\Tools\tool.exe -x");
    }

    [Test]
    public void Compose_Posix_EscapesEmbeddedSingleQuote()
    {
        var command = Command("/bin/echo", "it's");

        ShellCommandComposer.Compose(Bash, command).Line
            .Should().Be(PosixReveal + @"/bin/echo 'it'\''s'");
    }

    [Test]
    public void Compose_PowerShell_DoublesEmbeddedSingleQuote()
    {
        var command = Command("echo", "it's");

        ShellCommandComposer.Compose(PowerShell, command).Line
            .Should().Be(PowerShellReveal + "echo 'it''s'");
    }

    [Test]
    public void Compose_DollarSign_IsQuotedLiterally()
    {
        // Single quotes keep $ literal in PowerShell and POSIX shells alike.
        var command = Command("echo", "$HOME");

        ShellCommandComposer.Compose(PowerShell, command).Line
            .Should().Be(PowerShellReveal + "echo '$HOME'");
        ShellCommandComposer.Compose(Bash, command).Line
            .Should().Be(PosixReveal + "echo '$HOME'");
    }

    [Test]
    public void Compose_EmptyArgument_IsQuoted()
    {
        var command = Command("tool", "");

        ShellCommandComposer.Compose(Bash, command).Line
            .Should().Be(PosixReveal + "tool ''");
        ShellCommandComposer.Compose(PowerShell, command).Line
            .Should().Be(PowerShellReveal + @"tool """"");
    }

    [Test]
    public void Compose_ReadyMarker_ClearsThenEmitsTheMarker()
    {
        var command = Command("celbridge-py");

        // PowerShell writes a single character, from its code point so the echoed line does not carry it.
        ShellCommandComposer.Compose(PowerShell, command).Line
            .Should().Be("Clear-Host; Write-Host -NoNewline ([char]0x2404); celbridge-py");

        // POSIX emits an invisible OSC via printf octal escapes rather than visible text.
        ShellCommandComposer.Compose(Bash, command).Line
            .Should().Be(@"clear; printf '\033]7000;CELBRIDGE-CONSOLE-READY\007'; celbridge-py");
    }

    [Test]
    public void Compose_ReadyMarker_PowerShellScansForASingleCharacter()
    {
        // A single character cannot arrive split across two chunks.
        var composed = ShellCommandComposer.Compose(PowerShell, Command("celbridge-py"));

        composed.ScanMarker.Should().Be(Sentinel.ToString());
        composed.ScanMarker!.Length.Should().Be(1);
    }

    [Test]
    public void Compose_ReadyMarker_PosixScanMarkerIsTheInvisibleOscBytes()
    {
        var composed = ShellCommandComposer.Compose(Bash, Command("celbridge-py"));

        composed.ScanMarker.Should().Be($"{Escape}]7000;{PosixMarkerText}{Bell}");
    }

    [Test]
    public void Compose_ReadyMarker_PersistsOnScreenOnlyWhereItIsWrittenAsText()
    {
        // A marker in a screen cell comes back on every reflow of that row.
        var command = Command("celbridge-py");

        ShellCommandComposer.Compose(PowerShell, command)
            .MarkerPersistsOnScreen.Should().BeTrue();
        ShellCommandComposer.Compose(Bash, command)
            .MarkerPersistsOnScreen.Should().BeFalse();
    }

    [Test]
    public void Compose_ReadyMarker_ComposedLineNeverContainsTheScanMarker()
    {
        // The shell echoes this line as it reads it, before executing the write. If the echo contained the
        // scan marker the document would reveal the terminal before the clear had run.
        var command = Command("celbridge-py");

        foreach (var shell in new[] { PowerShell, Bash, Zsh })
        {
            var composed = ShellCommandComposer.Compose(shell, command);
            composed.ScanMarker.Should().NotBeNull();
            composed.Line.Should().NotContain(composed.ScanMarker!);
        }
    }

    [Test]
    public void Compose_ReadyMarker_CmdClearsWithoutAMarker()
    {
        var command = Command("celbridge-py");

        var composed = ShellCommandComposer.Compose(Cmd, command);

        composed.Line.Should().Be("cls & celbridge-py");
        composed.ScanMarker.Should().BeNull();
        composed.MarkerPersistsOnScreen.Should().BeFalse();
    }

    [Test]
    public void Compose_ReadyMarker_GoesBeforeTheCallOperator()
    {
        var command = Command(@"C:\Program Files\App\tool.exe");

        ShellCommandComposer.Compose(PowerShell, command).Line
            .Should().Be(PowerShellReveal + @"& 'C:\Program Files\App\tool.exe'");
    }

    [Test]
    public void Compose_ReadyMarker_PosixPlainShellRevealsWithoutACommand()
    {
        var composed = ShellCommandComposer.Compose(Bash, ConsoleStartupInvocation.None);

        composed.Line.Should().Be(@"clear; printf '\033]7000;CELBRIDGE-CONSOLE-READY\007';");
        composed.ScanMarker.Should().Be($"{Escape}]7000;{PosixMarkerText}{Bell}");
    }

    [Test]
    public void Compose_ReadyMarker_PowerShellPlainShellStaysEmpty()
    {
        var composed = ShellCommandComposer.Compose(PowerShell, ConsoleStartupInvocation.None);

        composed.Line.Should().BeEmpty();
        composed.ScanMarker.Should().BeNull();
    }

    [Test]
    public void Compose_WorkingDirectory_SetsTheLocationBeforeTheCommand()
    {
        var command = Command("celbridge-py");

        ShellCommandComposer.Compose(PowerShell, command, @"C:\Projects\Demo").Line
            .Should().Be(PowerShellReveal + @"Set-Location -LiteralPath 'C:\Projects\Demo'; celbridge-py");
        ShellCommandComposer.Compose(Bash, command, "/home/demo").Line
            .Should().Be(PosixReveal + "cd '/home/demo'; celbridge-py");
        ShellCommandComposer.Compose(Cmd, command, @"C:\Projects\Demo").Line
            .Should().Be(CmdReveal + @"cd /d ""C:\Projects\Demo"" & celbridge-py");
    }

    [Test]
    public void Compose_WorkingDirectory_QuotesAPathWithSpaces()
    {
        var command = Command("celbridge-py");

        ShellCommandComposer.Compose(PowerShell, command, @"C:\My Projects\Demo").Line
            .Should().Be(PowerShellReveal + @"Set-Location -LiteralPath 'C:\My Projects\Demo'; celbridge-py");
        ShellCommandComposer.Compose(Bash, command, "/home/demo user").Line
            .Should().Be(PosixReveal + "cd '/home/demo user'; celbridge-py");
        ShellCommandComposer.Compose(Cmd, command, @"C:\My Projects\Demo").Line
            .Should().Be(CmdReveal + @"cd /d ""C:\My Projects\Demo"" & celbridge-py");
    }

    [Test]
    public void Compose_WorkingDirectory_QuotesAPathCarryingACharacterNeedsQuotingCallsSafe()
    {
        // PowerShell reads an unquoted comma in argument position as an array separator.
        var command = Command("celbridge-py");

        ShellCommandComposer.Compose(PowerShell, command, @"C:\Projects,2026\Demo").Line
            .Should().Be(PowerShellReveal + @"Set-Location -LiteralPath 'C:\Projects,2026\Demo'; celbridge-py");
    }

    [Test]
    public void Compose_NoWorkingDirectory_InjectsNoLocationChange()
    {
        var command = Command("celbridge-py");

        ShellCommandComposer.Compose(PowerShell, command).Line
            .Should().Be(PowerShellReveal + "celbridge-py");
        ShellCommandComposer.Compose(PowerShell, command, "   ").Line
            .Should().Be(PowerShellReveal + "celbridge-py");
    }

    [Test]
    public void Compose_WorkingDirectory_PlainShellIsUnaffected()
    {
        var composed = ShellCommandComposer.Compose(PowerShell, ConsoleStartupInvocation.None, @"C:\Projects\Demo");

        composed.Line.Should().BeEmpty();
    }

    [Test]
    public void Compose_ZshPrompt_ReplacesTheStockPromptUnderZshOnly()
    {
        // The POSIX family shares a quoting dialect, but this syntax is zsh's own.
        var command = Command("celbridge-py");

        ShellCommandComposer.Compose(Zsh, command).Line
            .Should().Be(ZshReveal + "celbridge-py");
        ShellCommandComposer.Compose(Bash, command).Line
            .Should().Be(PosixReveal + "celbridge-py");
    }

    [Test]
    public void Compose_ZshPrompt_GuardMatchesTheStockMacOSPrompt()
    {
        // The exact prompt macOS sets in /etc/zshrc. A prompt set anywhere else fails this comparison,
        // which is what leaves a customized prompt alone.
        const string stockPrompt = "%n@%m %1~ %# ";

        ShellCommandComposer.Compose(Zsh, ConsoleStartupInvocation.None).Line
            .Should().Contain($"[[ $PROMPT == '{stockPrompt}' ]]");
    }

    [Test]
    public void Compose_ZshPrompt_PlainShellRevealsWithTheCompactPrompt()
    {
        var composed = ShellCommandComposer.Compose(Zsh, ConsoleStartupInvocation.None);

        composed.Line.Should().Be(ZshReveal.TrimEnd());
        composed.ScanMarker.Should().Be($"{Escape}]7000;{PosixMarkerText}{Bell}");
    }

    [Test]
    public void Compose_ZshPrompt_PrecedesTheWorkingDirectoryChange()
    {
        var command = Command("celbridge-py");

        ShellCommandComposer.Compose(Zsh, command, "/home/demo").Line
            .Should().Be(ZshReveal + "cd '/home/demo'; celbridge-py");
    }
}
