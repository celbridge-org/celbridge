using Celbridge.Console;
using Celbridge.Console.Helpers;

namespace Celbridge.Tests.Console;

[TestFixture]
public class ConsoleStartupFilesTests
{
    private static readonly ConsoleShell Zsh = new("/bin/zsh", ConsoleShellFamily.Posix);
    private static readonly ConsoleShell Bash = new("/bin/bash", ConsoleShellFamily.Posix);
    private static readonly ConsoleShell PowerShell = new("powershell.exe", ConsoleShellFamily.PowerShell);

    [Test]
    public void Generate_FillsInEveryToken()
    {
        foreach (var shell in new[] { Zsh, Bash, PowerShell })
        {
            foreach (var file in ConsoleStartupFiles.Generate(shell))
            {
                file.Content.Should().NotContain("{{", $"{file.RelativePath} has a token left in it");
            }
        }
    }

    // The scripts name the start-up's variables and the ready marker directly, so they must match the values
    // Celbridge uses.
    [Test]
    public void Scripts_UseTheSameVariablesAndMarkerAsCelbridge()
    {
        var sharedVariables = new[]
        {
            ConsoleStartupFiles.RestoreVariable,
            ConsoleStartupFiles.HistoryVariable,
            ConsoleStartupFiles.CommandVariable,
            ConsoleStartupFiles.WorkingFolderVariable,
            ConsoleStartupFiles.UseShellProfileVariable,
            ConsoleStartupFiles.CompactPromptVariable,
            ConsoleStartupFiles.VariablePrefix,
            ConsoleEnvironmentVariables.PathFolders,
        };
        var powerShellMarker = $"[char]0x{(int)ConsoleReadyMarker.PowerShellCharacter:x4}";

        var zsh = AllContent(Zsh);
        var bash = AllContent(Bash);
        var powerShell = AllContent(PowerShell);

        foreach (var variable in sharedVariables)
        {
            zsh.Should().Contain(variable);
            bash.Should().Contain(variable);
            powerShell.Should().Contain(variable);
        }
        zsh.Should().Contain(ConsoleStartupFiles.UserZdotdirVariable);
        zsh.Should().Contain(ConsoleReadyMarker.PosixPrintfSource);
        bash.Should().Contain(ConsoleReadyMarker.PosixPrintfSource);
        powerShell.Should().Contain(powerShellMarker);
    }

    private static string AllContent(ConsoleShell shell)
    {
        return string.Concat(ConsoleStartupFiles.Generate(shell).Select(file => file.Content));
    }

    [Test]
    public void BuildRestoreList_IncludesPath_OnlyWhenTheConsoleSetsIt()
    {
        var names = new[] { "UV_CACHE_DIR", "PATH", "MY_TABLE" };

        ConsoleStartupFiles.BuildRestoreList(names, restoresPath: false).Should().Be("MY_TABLE UV_CACHE_DIR");
        ConsoleStartupFiles.BuildRestoreList(names, restoresPath: true).Should().Be("MY_TABLE PATH UV_CACHE_DIR");
    }

    [Test]
    public void BuildRestoreList_LeavesOutTheStartUpsOwnVariablesAndNamesNoShellCanHold()
    {
        var names = new[]
        {
            "CELBRIDGE_CONSOLE_PATH_FOLDERS",
            "ProgramFiles(x86)",
            "1_STARTS_WITH_A_DIGIT",
            "CELBRIDGE_PYTHON_VERSION",
        };

        ConsoleStartupFiles.BuildRestoreList(names, restoresPath: true).Should().Be("CELBRIDGE_PYTHON_VERSION");
    }
}
