using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Celbridge.Console;
using Celbridge.Console.Helpers;

namespace Celbridge.Tests.Console;

/// <summary>
/// Starts real shells on the generated start-up files, with a hostile environment and a decoy profile in a
/// decoy home, and reads back what the first prompt sees. Each check prints its result through printf, so the
/// shell's echo of the command, which carries the format string, never matches.
/// </summary>
[TestFixture]
public class ConsoleStartupShellTests
{
    private const string CelbridgeBin = "/celbridge/bin";
    private const string CelbridgeToolBin = "/celbridge/uv_bin";
    private const string CelbridgeCache = "/celbridge/cache";

    private static readonly Regex ResultPattern = new(@"CELTEST (\w+)=(.*)$", RegexOptions.Multiline);

    private string _root = null!;
    private string _home = null!;
    private string _historyFolder = null!;

    [SetUp]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "celbridge-startup-tests", Path.GetRandomFileName());
        _home = Path.Combine(_root, "home");
        _historyFolder = Path.Combine(_root, "data", "console", "history");
        Directory.CreateDirectory(_home);
        Directory.CreateDirectory(_historyFolder);

        WriteDecoyProfile(Path.Combine(_home, ".zshrc"), "typeset -U path PATH\n");
        WriteDecoyProfile(Path.Combine(_home, ".bash_profile"), "PROMPT_COMMAND=\"history -a;\"\n");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestCase("zsh")]
    [TestCase("bash")]
    public void PassThrough_ProfileRuns_ThenCelbridgeSettingsWin(string shellName)
    {
        var results = RunShell(shellName, useShellProfile: true);

        results["DECOY_PROFILE"].Should().Be("1", "the user's profile runs in pass-through mode");
        results["VIRTUAL_ENV"].Should().Be("<unset>");
        results["UV_PYTHON"].Should().Be("<unset>");
        results["PYTHONPATH"].Should().Be("<unset>");
        results["UV_INDEX_URL"].Should().Be("https://mirror.example", "uv's index settings pass through");
        results["UV_CACHE_DIR"].Should().Be(CelbridgeCache, "Celbridge's value goes back on top");
        results["MY_TABLE"].Should().Be("table", "the console's own table goes back on top");
        results["PATH"].Should().StartWith($"{CelbridgeBin}:{CelbridgeToolBin}:");
        results["UV_COMMAND"].Should().BeEmpty("no alias stands in for uv");
        results["CELBRIDGE_PY_COMMAND"].Should().BeEmpty("no function stands in for celbridge-py");
        results["CELBRIDGE_CONSOLE_RESTORE"].Should().Be("<unset>");
    }

    [TestCase("zsh")]
    [TestCase("bash")]
    public void Clean_ProfileDoesNotRun(string shellName)
    {
        var results = RunShell(shellName, useShellProfile: false);

        results["DECOY_PROFILE"].Should().Be("<unset>");
        results["UV_CACHE_DIR"].Should().Be(CelbridgeCache);
        results["MY_TABLE"].Should().Be("table");
        results["PATH"].Should().StartWith($"{CelbridgeBin}:{CelbridgeToolBin}:");
    }

    // Closing a console kills its shell, which then has no chance to save, so the shell is killed here too.
    [TestCase("zsh", "zsh_history")]
    [TestCase("bash", "bash_history")]
    public void History_LandsInTheProjectFile_AsEachCommandIsEntered(string shellName, string historyFileName)
    {
        RunShell(shellName, useShellProfile: true, "echo celbridge-history-marker\nkill -9 $$");

        var historyText = File.ReadAllText(Path.Combine(_historyFolder, historyFileName));
        historyText.Should().Contain("celbridge-history-marker");
        File.Exists(Path.Combine(_root, "decoy_history")).Should().BeFalse("the profile's own history file is overridden");
    }

    [TestCase("zsh", "zsh_history")]
    [TestCase("bash", "bash_history")]
    public void History_AppendsToTheProjectFile_KeepingEarlierConsolesCommands(string shellName, string historyFileName)
    {
        var historyPath = Path.Combine(_historyFolder, historyFileName);
        File.WriteAllText(historyPath, "echo an-earlier-console\n");

        RunShell(shellName, useShellProfile: true, "echo celbridge-history-marker\nkill -9 $$");

        var historyText = File.ReadAllText(historyPath);
        historyText.Should().Contain("an-earlier-console");
        historyText.Should().Contain("celbridge-history-marker");
    }

    [Test]
    public void Zsh_UserZdotdir_IsReadAndHandedBack()
    {
        var userZdotdir = Path.Combine(_root, "zdotdir");
        Directory.CreateDirectory(userZdotdir);
        File.Move(Path.Combine(_home, ".zshrc"), Path.Combine(userZdotdir, ".zshrc"));

        var results = RunShell("zsh", useShellProfile: true, userZdotdir: userZdotdir);

        results["DECOY_PROFILE"].Should().Be("1", "the user's files are read from their own ZDOTDIR");
        results["ZDOTDIR"].Should().Be(userZdotdir, "anything started from the console reads the user's own files");
    }

    [Test]
    public void PowerShell_FirstPrompt_PutsCelbridgeSettingsBackOnTop()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("PowerShell consoles run on Windows only.");
        }

        // What a profile might do, then the prompt PowerShell draws before it reads the first command.
        var script = ConsoleStartupFiles.BuildPowerShellStartup(useShellProfile: false) + """

            $env:VIRTUAL_ENV = 'C:\decoy\venv'
            $env:UV_CACHE_DIR = 'C:\decoy\cache'
            $env:UV_INDEX_URL = 'https://mirror.example'
            $env:MY_TABLE = 'profile'
            $env:Path = 'C:\decoy\bin;' + $env:Path
            Set-Alias -Name uv -Value 'C:\decoy\bin\uv.exe' -Scope Global
            function global:celbridge-py { 'decoy' }
            prompt | Out-Null
            "CELTEST VIRTUAL_ENV=$env:VIRTUAL_ENV"
            "CELTEST UV_CACHE_DIR=$env:UV_CACHE_DIR"
            "CELTEST UV_INDEX_URL=$env:UV_INDEX_URL"
            "CELTEST MY_TABLE=$env:MY_TABLE"
            "CELTEST PATH=$env:Path"
            "CELTEST UV_ALIAS=$(Test-Path Alias:uv)"
            "CELTEST CELBRIDGE_PY_FUNCTION=$(Test-Path Function:celbridge-py)"
            "CELTEST RESTORE=$env:CELBRIDGE_CONSOLE_RESTORE"
            """;
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", encodedScript })
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment["UV_CACHE_DIR"] = @"C:\celbridge\cache";
        startInfo.Environment["MY_TABLE"] = "table";
        startInfo.Environment[ConsoleStartupFiles.RestoreVariable] = "MY_TABLE UV_CACHE_DIR";
        startInfo.Environment[ConsoleEnvironmentVariables.PathFolders] = @"C:\celbridge\bin;C:\celbridge\uv_bin";

        var results = Run(startInfo, standardInput: null);

        results["VIRTUAL_ENV"].Should().BeEmpty();
        results["UV_CACHE_DIR"].Should().Be(@"C:\celbridge\cache");
        results["UV_INDEX_URL"].Should().Be("https://mirror.example");
        results["MY_TABLE"].Should().Be("table");
        results["PATH"].Should().StartWith(@"C:\celbridge\bin;C:\celbridge\uv_bin;");
        results["UV_ALIAS"].Should().Be("False");
        results["CELBRIDGE_PY_FUNCTION"].Should().Be("False");
        results["RESTORE"].Should().BeEmpty();
    }

    // A profile that would steer uv and Python away from Celbridge's install if it won, with a line of shell
    // specific set-up first.
    private void WriteDecoyProfile(string path, string shellSpecific)
    {
        var profile = shellSpecific +
            "export PATH=/decoy/bin:$PATH\n" +
            "export VIRTUAL_ENV=/decoy/venv UV_CACHE_DIR=/decoy/cache UV_PYTHON=/decoy/python\n" +
            "export UV_INDEX_URL=https://mirror.example PYTHONPATH=/decoy/pythonpath\n" +
            "export DECOY_PROFILE=1 MY_TABLE=profile\n" +
            "alias uv=/decoy/bin/uv\n" +
            "celbridge-py() { echo decoy; }\n" +
            $"HISTFILE={Path.Combine(_root, "decoy_history")}\n";
        File.WriteAllText(path, profile);
    }

    private Dictionary<string, string> RunShell(
        string shellName,
        bool useShellProfile,
        string extraCommand = "",
        string? userZdotdir = null)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("zsh and bash consoles run on macOS and Linux.");
        }

        var executable = $"/bin/{shellName}";
        if (!File.Exists(executable))
        {
            Assert.Ignore($"{executable} is not installed.");
        }

        var modeFolder = Path.Combine(_root, "data", "console", useShellProfile ? "pass_through" : "clean");
        foreach (var file in ConsoleStartupFiles.Generate(useShellProfile))
        {
            var filePath = Path.Combine(modeFolder, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, file.Content);
        }

        var shell = new ConsoleShell(executable, ConsoleShellFamily.Posix);
        var options = new ConsoleStartupOptions(modeFolder, _historyFolder, useShellProfile, userZdotdir);
        var launch = ConsoleShellLaunch.Build(shell, options);

        // Input from a pipe rather than a terminal, so the shell is told it is interactive.
        var commandLine = shell.IsZsh ? launch.CommandLine + " -i" : launch.CommandLine;
        var startInfo = new ProcessStartInfo("/bin/sh")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(commandLine);

        // The environment Celbridge builds, as a console session would pass it.
        startInfo.Environment.Clear();
        startInfo.Environment["HOME"] = _home;
        startInfo.Environment["USER"] = Environment.UserName;
        startInfo.Environment["PATH"] = $"{CelbridgeBin}:{CelbridgeToolBin}:/usr/bin:/bin";
        startInfo.Environment["UV_CACHE_DIR"] = CelbridgeCache;
        startInfo.Environment["MY_TABLE"] = "table";
        startInfo.Environment["BASH_SILENCE_DEPRECATION_WARNING"] = "1";
        startInfo.Environment[ConsoleStartupFiles.RestoreVariable] = "MY_TABLE UV_CACHE_DIR";
        startInfo.Environment[ConsoleEnvironmentVariables.PathFolders] = $"{CelbridgeBin}:{CelbridgeToolBin}";
        foreach (var pair in launch.Environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        var names = new[]
        {
            "DECOY_PROFILE",
            "VIRTUAL_ENV",
            "UV_PYTHON",
            "PYTHONPATH",
            "UV_INDEX_URL",
            "UV_CACHE_DIR",
            "MY_TABLE",
            "PATH",
            "ZDOTDIR",
            "CELBRIDGE_CONSOLE_RESTORE",
        };
        var input = new StringBuilder();
        foreach (var name in names)
        {
            input.Append($"printf 'CEL%s %s=%s\\n' TEST {name} \"${{{name}-<unset>}}\"\n");
        }
        input.Append("printf 'CEL%s %s=%s\\n' TEST UV_COMMAND \"$(command -v uv)\"\n");
        input.Append("printf 'CEL%s %s=%s\\n' TEST CELBRIDGE_PY_COMMAND \"$(command -v celbridge-py)\"\n");
        if (!string.IsNullOrEmpty(extraCommand))
        {
            input.Append(extraCommand + "\n");
        }
        input.Append("exit\n");

        return Run(startInfo, input.ToString());
    }

    private static Dictionary<string, string> Run(ProcessStartInfo startInfo, string? standardInput)
    {
        using var process = Process.Start(startInfo)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        if (standardInput is not null)
        {
            process.StandardInput.Write(standardInput);
            process.StandardInput.Close();
        }

        if (!process.WaitForExit(20000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("The shell did not exit within 20 seconds.");
        }

        var output = outputTask.Result + errorTask.Result;
        var results = new Dictionary<string, string>();
        foreach (Match match in ResultPattern.Matches(output))
        {
            results[match.Groups[1].Value] = match.Groups[2].Value.TrimEnd('\r');
        }

        results.Should().NotBeEmpty($"the shell printed no results. It printed:\n{output}");

        return results;
    }
}
