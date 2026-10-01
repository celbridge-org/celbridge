using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Celbridge.Console;
using Celbridge.Console.Helpers;

namespace Celbridge.Tests.Console;

/// <summary>
/// Starts real shells with the generated start-up files. Each shell gets a hostile environment and a decoy
/// profile in a decoy home. The tests read back what the first prompt sees, and the prompt at a later one.
/// Each check prints its result with printf. The shell's echo of the command shows only the format string, so
/// the echo never matches.
/// </summary>
[TestFixture]
public class ConsoleStartupShellTests
{
    private const string CelbridgeBin = "/celbridge/bin";
    private const string CelbridgeToolBin = "/celbridge/uv_bin";
    private const string CelbridgeCache = "/celbridge/cache";
    private const string DecoyPrompt = "decoy> ";
    private const string ZshCompactPrompt = "%F{cyan}%1~%f %# ";
    private const string BashCompactPrompt = @"\[\e[36m\]\W\[\e[m\] \$ ";

    // Reads, at a later prompt, whether the decoy's prompt command ran and whether the first-prompt hook is still
    // registered.
    private const string PromptCommandChecks = """
        printf 'CEL%s %s=%s\n' TEST MARK "${DECOY_MARK-<unset>}"
        case "${PROMPT_COMMAND[*]}" in *_celbridge_first_prompt*) printf 'CEL%s %s=%s\n' TEST HOOK_LEFT yes ;; *) printf 'CEL%s %s=%s\n' TEST HOOK_LEFT no ;; esac
        """;

    private static readonly string PosixMarker = ConsoleReadyMarker.For(new ConsoleShell("/bin/zsh", ConsoleShellFamily.Posix), hasCommand: false)!.ScanText;

    private static readonly Regex ResultPattern = new(@"CELTEST (\w+)=(.*)$", RegexOptions.Multiline);

    private string _root = null!;
    private string _home = null!;
    private string _historyFolder = null!;

    // What the last shell wrote to its standard output, in order.
    private string _standardOutput = string.Empty;

    [SetUp]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "celbridge-startup-tests", Path.GetRandomFileName());
        _home = Path.Combine(_root, "home");
        _historyFolder = Path.Combine(_root, "data", "console", "history");
        Directory.CreateDirectory(_home);
        Directory.CreateDirectory(_historyFolder);

        WriteDecoyProfile(Path.Combine(_home, ".zshrc"), "typeset -U path PATH\nprecmd_functions+=(decoy_prompt)\n");
        WriteDecoyProfile(Path.Combine(_home, ".bash_profile"), "PROMPT_COMMAND=\"decoy_prompt; history -a;\"\n");
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

        results["DECOY_PROFILE"].Should().Be("1", "the user's profile runs when the shell profile is on");
        results["VIRTUAL_ENV"].Should().Be("<unset>");
        results["UV_PYTHON"].Should().Be("<unset>");
        results["PYTHONPATH"].Should().Be("<unset>");
        results["UV_INDEX_URL"].Should().Be("https://mirror.example", "uv's index settings are kept");
        results["UV_CACHE_DIR"].Should().Be(CelbridgeCache, "Celbridge's value is restored");
        results["MY_TABLE"].Should().Be("table", "the console's own value is restored");
        results["PATH"].Should().StartWith($"{CelbridgeBin}:{CelbridgeToolBin}:");
        results["UV_COMMAND"].Should().BeEmpty("no alias replaces uv");
        results["CELBRIDGE_PY_COMMAND"].Should().BeEmpty("no function replaces celbridge-py");
        results["CELBRIDGE_CONSOLE_RESTORE"].Should().Be("<unset>");
        _standardOutput.Should().Contain(PosixMarker, "a console with no command is still shown");
    }

    // The start-up splits the command only at line breaks. No argument is quoted, split or expanded, and an
    // empty argument is kept.
    [TestCase("zsh")]
    [TestCase("bash")]
    public void Command_RunsAfterTheMarker_WithEachArgumentIntact(string shellName)
    {
        const string awkwardArgument = "Some string, it's \"quoted\" $HOME * ; `true`";
        var command = new[]
        {
            "/usr/bin/printf",
            @"CELTEST %s=[%s]\nCELTEST %s=[%s]\n",
            "COMMAND",
            awkwardArgument,
            "EMPTY",
            "",
        };

        var results = RunShell(shellName, useShellProfile: true, command: command);

        results["COMMAND"].Should().Be($"[{awkwardArgument}]");
        results["EMPTY"].Should().Be("[]");
        var markerIndex = _standardOutput.IndexOf(PosixMarker, StringComparison.Ordinal);
        markerIndex.Should().BeGreaterThanOrEqualTo(0);
        _standardOutput.IndexOf("CELTEST COMMAND=", StringComparison.Ordinal).Should().BeGreaterThan(markerIndex);
    }

    // The decoy profile leaves the shell in another folder, as a profile's cd would.
    [TestCase("zsh")]
    [TestCase("bash")]
    public void Command_RunsInTheWorkingFolder_WithoutTheStartUpsVariables(string shellName)
    {
        File.AppendAllText(Path.Combine(_home, shellName == "zsh" ? ".zshrc" : ".bash_profile"), "cd /\n");
        var workingFolder = Path.Combine(_root, "work");
        Directory.CreateDirectory(workingFolder);
        var command = new[]
        {
            "/bin/sh",
            "-c",
            "printf 'CELTEST FOLDER=%s\\n' \"$(pwd -P)\"; printf 'CELTEST INHERITED=%s\\n' \"${CELBRIDGE_CONSOLE_COMMAND-<unset>}\"",
        };

        var results = RunShell(shellName, useShellProfile: true, command: command, workingFolder: workingFolder);

        // The temporary folder can sit behind a symbolic link, which pwd -P resolves.
        results["FOLDER"].Should().EndWith(Path.Combine(Path.GetFileName(_root), "work"));
        results["INHERITED"].Should().Be("<unset>", "the command starts without the start-up's variables");
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

    // Closing a console kills its shell before it can save. The test kills the shell the same way.
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

    // The console's command and the marker run from the start-up files, so neither is typed at the prompt.
    [TestCase("zsh", "zsh_history")]
    [TestCase("bash", "bash_history")]
    public void History_HoldsNoneOfTheStartUp(string shellName, string historyFileName)
    {
        var command = new[] { "/usr/bin/true", "celbridge-command-word" };

        RunShell(shellName, useShellProfile: true, "echo celbridge-history-marker\nkill -9 $$", command: command);

        var historyText = File.ReadAllText(Path.Combine(_historyFolder, historyFileName));
        historyText.Should().Contain("celbridge-history-marker");
        historyText.Should().NotContain("celbridge-command-word");
        historyText.Should().NotContain("CELBRIDGE-CONSOLE-READY");
        historyText.Should().NotContain("clear");
    }

    // The decoy profile resets its own prompt before each prompt, as a prompt framework does.
    [TestCase("zsh", true, "%F{cyan}%1~%f %# ")]
    [TestCase("zsh", false, "%F{cyan}%1~%f %# ")]
    [TestCase("bash", true, @"\[\e[36m\]\W\[\e[m\] \$ ")]
    [TestCase("bash", false, @"\[\e[36m\]\W\[\e[m\] \$ ")]
    public void CompactPrompt_IsSetBeforeEveryPrompt(string shellName, bool useShellProfile, string compactPrompt)
    {
        var results = RunShell(shellName, useShellProfile);

        results["FIRST_PROMPT"].Should().Be(compactPrompt);
        results["LATER_PROMPT"].Should().Be(compactPrompt, "the compact prompt is set after the profile's own hook");
    }

    [TestCase("zsh")]
    [TestCase("bash")]
    public void CompactPromptOff_LeavesTheProfilesPrompt(string shellName)
    {
        var results = RunShell(shellName, useShellProfile: true, compactPrompt: false);

        results["FIRST_PROMPT"].Should().Be(DecoyPrompt);
        results["LATER_PROMPT"].Should().Be(DecoyPrompt);
    }

    [Test]
    public void Zsh_UserZdotdir_IsReadAndHandedBack()
    {
        var userZdotdir = Path.Combine(_root, "zdotdir");
        Directory.CreateDirectory(userZdotdir);
        File.Move(Path.Combine(_home, ".zshrc"), Path.Combine(userZdotdir, ".zshrc"));

        var results = RunShell("zsh", useShellProfile: true, userZdotdir: userZdotdir);

        results["DECOY_PROFILE"].Should().Be("1", "the user's files are read from their own ZDOTDIR");
        results["ZDOTDIR"].Should().Be(userZdotdir, "a shell started from the console reads the user's own files");
    }

    [Test]
    public void Zsh_CompactPrompt_SurvivesAProfileThatReplacesTheHookList()
    {
        File.AppendAllText(Path.Combine(_home, ".zshrc"), "precmd_functions=(decoy_prompt)\n");

        var results = RunShell("zsh", useShellProfile: true);

        results["FIRST_PROMPT"].Should().Be(ZshCompactPrompt);
        results["LATER_PROMPT"].Should().Be(ZshCompactPrompt);
    }

    // A hook that stopped at an unset variable would restore nothing. Each hook resets the options it relies on
    // while it runs.
    [TestCase("zsh", "setopt NO_UNSET\n")]
    [TestCase("bash", "set -u\n")]
    public void FirstPrompt_Restores_WhenTheProfileMakesUnsetVariablesAnError(string shellName, string options)
    {
        File.AppendAllText(Path.Combine(_home, shellName == "zsh" ? ".zshrc" : ".bash_profile"), options);

        var results = RunShell(shellName, useShellProfile: true, compactPrompt: false);

        results["VIRTUAL_ENV"].Should().Be("<unset>");
        results["UV_CACHE_DIR"].Should().Be(CelbridgeCache);
        results["CELBRIDGE_CONSOLE_RESTORE"].Should().Be("<unset>");
        _standardOutput.Should().Contain(PosixMarker);
    }

    // bash-preexec rearranges PROMPT_COMMAND at the first prompt when it installs itself. The decoy does the same:
    // it puts decoy_mark first, and decoy_mark records that it ran.
    [Test]
    public void Bash_FirstPromptHook_KeepsChangesAnotherPromptCommandMakes()
    {
        File.AppendAllText(Path.Combine(_home, ".bash_profile"), """
            decoy_mark() { DECOY_MARK=1; }
            decoy_rearrange() { [ -n "${DECOY_DONE-}" ] && return; DECOY_DONE=1; PROMPT_COMMAND="decoy_mark"$'\n'"$PROMPT_COMMAND"; }
            PROMPT_COMMAND="$PROMPT_COMMAND"$'\n'"decoy_rearrange"

            """);

        var results = RunShell("bash", useShellProfile: true, PromptCommandChecks);

        results["MARK"].Should().Be("1", "the decoy's change to PROMPT_COMMAND is kept");
        results["HOOK_LEFT"].Should().Be("no");
        results["LATER_PROMPT"].Should().Be(BashCompactPrompt);
    }

    [Test]
    public void Bash_FirstPromptHook_KeepsChangesAnotherPromptCommandMakes_InAnArray()
    {
        if (!BashHasPromptCommandArrays())
        {
            Assert.Ignore("PROMPT_COMMAND can be an array only from bash 5.1, on macOS and Linux.");
        }

        File.AppendAllText(Path.Combine(_home, ".bash_profile"), """
            decoy_mark() { DECOY_MARK=1; }
            decoy_rearrange() { [ -n "${DECOY_DONE-}" ] && return; DECOY_DONE=1; PROMPT_COMMAND=(decoy_mark "${PROMPT_COMMAND[@]}"); }
            PROMPT_COMMAND=(decoy_prompt "history -a" decoy_rearrange)

            """);

        var results = RunShell("bash", useShellProfile: true, PromptCommandChecks);

        results["MARK"].Should().Be("1", "the decoy's change to PROMPT_COMMAND is kept");
        results["HOOK_LEFT"].Should().Be("no");
        results["LATER_PROMPT"].Should().Be(BashCompactPrompt);
    }

    [Test]
    public void PowerShell_FirstPrompt_PutsCelbridgeSettingsBackOnTop()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("PowerShell consoles run on Windows only.");
        }

        // Acts like a profile, then draws the prompt PowerShell shows before the first command.
        var script = ConsoleStartupFiles.BuildPowerShellStartup() + """

            $env:VIRTUAL_ENV = 'C:\decoy\venv'
            $env:UV_CACHE_DIR = 'C:\decoy\cache'
            $env:UV_INDEX_URL = 'https://mirror.example'
            $env:MY_TABLE = 'profile'
            $env:Path = 'C:\decoy\bin;' + $env:Path
            Set-Alias -Name uv -Value 'C:\decoy\bin\uv.exe' -Scope Global
            function global:celbridge-py { 'decoy' }
            $global:celbridgeRoot = 'mine'
            prompt | Out-Null
            "CELTEST USER_GLOBAL=$global:celbridgeRoot"
            "CELTEST VIRTUAL_ENV=$env:VIRTUAL_ENV"
            "CELTEST UV_CACHE_DIR=$env:UV_CACHE_DIR"
            "CELTEST UV_INDEX_URL=$env:UV_INDEX_URL"
            "CELTEST MY_TABLE=$env:MY_TABLE"
            "CELTEST PATH=$env:Path"
            "CELTEST UV_ALIAS=$(Test-Path Alias:uv)"
            "CELTEST CELBRIDGE_PY_FUNCTION=$(Test-Path Function:celbridge-py)"
            "CELTEST RESTORE=$env:CELBRIDGE_CONSOLE_RESTORE"
            $leftBehind = @((Get-ChildItem Function:Celbridge*, Variable:Celbridge*).Name) -ne 'celbridgeRoot'
            "CELTEST LEFT_BEHIND=$leftBehind"
            """;
        var results = RunPowerShell(script, new Dictionary<string, string>
        {
            ["UV_CACHE_DIR"] = @"C:\celbridge\cache",
            ["MY_TABLE"] = "table",
            [ConsoleStartupFiles.RestoreVariable] = "MY_TABLE UV_CACHE_DIR",
            [ConsoleEnvironmentVariables.PathFolders] = @"C:\celbridge\bin;C:\celbridge\uv_bin",
        });

        results["VIRTUAL_ENV"].Should().BeEmpty();
        results["UV_CACHE_DIR"].Should().Be(@"C:\celbridge\cache");
        results["UV_INDEX_URL"].Should().Be("https://mirror.example");
        results["MY_TABLE"].Should().Be("table");
        results["PATH"].Should().StartWith(@"C:\celbridge\bin;C:\celbridge\uv_bin;");
        results["UV_ALIAS"].Should().Be("False");
        results["CELBRIDGE_PY_FUNCTION"].Should().Be("False");
        results["RESTORE"].Should().BeEmpty();
        results["USER_GLOBAL"].Should().Be("mine", "the start-up removes only its own variables");
        results["LEFT_BEHIND"].Should().BeEmpty("the first prompt removes everything the start-up defined");
    }

    [TestCase(true, "PS work> ")]
    [TestCase(false, DecoyPrompt)]
    public void PowerShell_Prompt_IsCompactUnlessTurnedOff(bool compactPrompt, string expected)
    {
        var results = RunPowerShellOnDecoyProfile(useShellProfile: true, compactPrompt);

        results["FIRST_PROMPT"].Should().Be(expected);
        results["LATER_PROMPT"].Should().Be(expected);
    }

    // The decoy's prompt sets a virtual environment, as a tool's prompt hook might. Celbridge's settings must be
    // restored after that, at the first prompt.
    [Test]
    public void PowerShell_FirstPrompt_RestoresAfterTheProfilesPromptRuns()
    {
        var results = RunPowerShellOnDecoyProfile(useShellProfile: true, compactPrompt: false);

        results["FIRST_PROMPT"].Should().Be(DecoyPrompt);
        results["VIRTUAL_ENV"].Should().BeEmpty();
    }

    [TestCase(true, "1")]
    [TestCase(false, "")]
    public void PowerShell_Profile_RunsOnlyWhenTheConsoleUsesIt(bool useShellProfile, string expected)
    {
        var results = RunPowerShellOnDecoyProfile(useShellProfile, compactPrompt: false);

        results["DECOY_PROFILE"].Should().Be(expected);
    }

    // Writes a decoy profile that points uv and Python away from Celbridge's install, and sets its own prompt
    // before each prompt. The shell-specific set-up comes first.
    private void WriteDecoyProfile(string path, string shellSpecific)
    {
        var profile = shellSpecific +
            $"decoy_prompt() {{ PS1='{DecoyPrompt}'; }}\n" +
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
        string? userZdotdir = null,
        bool compactPrompt = true,
        IReadOnlyList<string>? command = null,
        string? workingFolder = null)
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

        var shell = new ConsoleShell(executable, ConsoleShellFamily.Posix);

        var startupFolder = Path.Combine(_root, "data", "console");
        foreach (var file in ConsoleStartupFiles.Generate(shell))
        {
            var filePath = Path.Combine(startupFolder, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, file.Content);
        }

        var options = new ConsoleStartupOptions(startupFolder, _historyFolder)
        {
            Command = command ?? Array.Empty<string>(),
            WorkingFolder = workingFolder,
            UseShellProfile = useShellProfile,
            CompactPrompt = compactPrompt,
            UserZdotdir = userZdotdir,
        };
        var launch = ConsoleShellLaunch.Build(shell, options);

        // Input comes from a pipe, not a terminal, so the shell must be told it is interactive.
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
        // In zsh, PS1 is the same as PROMPT. The first line runs at the first prompt, and each later line at a
        // new prompt.
        var input = new StringBuilder();
        input.Append("printf 'CEL%s %s=%s\\n' TEST FIRST_PROMPT \"$PS1\"\n");
        foreach (var name in names)
        {
            input.Append($"printf 'CEL%s %s=%s\\n' TEST {name} \"${{{name}-<unset>}}\"\n");
        }
        input.Append("printf 'CEL%s %s=%s\\n' TEST UV_COMMAND \"$(command -v uv)\"\n");
        input.Append("printf 'CEL%s %s=%s\\n' TEST CELBRIDGE_PY_COMMAND \"$(command -v celbridge-py)\"\n");
        input.Append("printf 'CEL%s %s=%s\\n' TEST LATER_PROMPT \"$PS1\"\n");
        if (!string.IsNullOrEmpty(extraCommand))
        {
            input.Append(extraCommand + "\n");
        }
        input.Append("exit\n");

        return Run(startInfo, input.ToString());
    }

    [Test]
    public void PowerShell_Command_RunsAfterTheMarker_InItsWorkingFolder()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("PowerShell consoles run on Windows only.");
        }

        var workingFolder = Path.Combine(_root, "work");
        Directory.CreateDirectory(workingFolder);
        var command = new[] { "cmd.exe", "/c", "echo", "CELTEST", "COMMAND=ran" };

        // After the start-up, the shell stays in the folder where the command ran. The lines below read it back.
        var script = ConsoleStartupFiles.BuildPowerShellStartup() + """

            "CELTEST FOLDER=$((Get-Location).Path)"
            "CELTEST INHERITED=$env:CELBRIDGE_CONSOLE_COMMAND"
            """;

        var environment = new Dictionary<string, string>
        {
            [ConsoleStartupFiles.CommandVariable] = string.Join('\n', command),
            [ConsoleStartupFiles.WorkingFolderVariable] = workingFolder,
        };

        var results = RunPowerShell(script, environment);

        results["COMMAND"].Should().Be("ran");
        results["FOLDER"].Should().Be(workingFolder);
        results["INHERITED"].Should().BeEmpty("the command starts without the start-up's variables");
        var markerIndex = _standardOutput.IndexOf(ConsoleReadyMarker.PowerShellCharacter);
        markerIndex.Should().BeGreaterThanOrEqualTo(0);
        _standardOutput.IndexOf("CELTEST COMMAND=", StringComparison.Ordinal).Should().BeGreaterThan(markerIndex);
    }

    // cmd's echo prints the command line it received, so the test sees the arguments exactly as the program does.
    // An empty argument, a quote inside an argument with a space, and a trailing backslash each need quoting that
    // Windows PowerShell does not do itself.
    [Test]
    public void PowerShell_Command_PassesAProgramsArgumentsQuoted()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("PowerShell consoles run on Windows only.");
        }

        var command = new[] { "cmd.exe", "/c", "echo", "CELTEST", "LINE=", "", "plain", "a b", "print(\"hi there\")", @"a b\" };
        var script = ConsoleStartupFiles.BuildPowerShellStartup() + """

            "CELTEST ARGUMENT_LINE=$env:CELBRIDGE_CONSOLE_ARGUMENTS"
            """;

        var results = RunPowerShell(script, new Dictionary<string, string>
        {
            [ConsoleStartupFiles.CommandVariable] = string.Join('\n', command),
        });

        results["LINE"].Should().Be(@" """" plain ""a b"" ""print(\""hi there\"")"" ""a b\\""");
        results["ARGUMENT_LINE"].Should().BeEmpty("the start-up removes the variable once the program has run");
    }

    // Runs the start-up in a working folder, with $PROFILE pointing only at a decoy profile. The start-up loads
    // the decoy as if it were the user's profile. The decoy sets its own variable, and a prompt that sets a
    // virtual environment each time it runs.
    private Dictionary<string, string> RunPowerShellOnDecoyProfile(bool useShellProfile, bool compactPrompt)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("PowerShell consoles run on Windows only.");
        }

        var profilePath = Path.Combine(_root, "profile.ps1");
        File.WriteAllText(profilePath,
            $"$env:DECOY_PROFILE = '1'\nfunction global:prompt {{ $env:VIRTUAL_ENV = 'C:\\decoy\\venv'; '{DecoyPrompt}' }}\n");
        var workFolder = Path.Combine(_root, "work");
        Directory.CreateDirectory(workFolder);

        var script = $$"""
            $PROFILE = [pscustomobject]@{ AllUsersAllHosts = $null; AllUsersCurrentHost = $null; CurrentUserAllHosts = '{{PowerShellQuote(profilePath)}}'; CurrentUserCurrentHost = $null }
            Set-Location -LiteralPath '{{PowerShellQuote(workFolder)}}'

            """ + ConsoleStartupFiles.BuildPowerShellStartup() + """

            "CELTEST FIRST_PROMPT=$(prompt)"
            "CELTEST VIRTUAL_ENV=$env:VIRTUAL_ENV"
            "CELTEST LATER_PROMPT=$(prompt)"
            "CELTEST DECOY_PROFILE=$env:DECOY_PROFILE"
            """;

        var environment = new Dictionary<string, string>();
        if (useShellProfile)
        {
            environment[ConsoleStartupFiles.UseShellProfileVariable] = "1";
        }
        if (compactPrompt)
        {
            environment[ConsoleStartupFiles.CompactPromptVariable] = "1";
        }

        return RunPowerShell(script, environment);
    }

    private Dictionary<string, string> RunPowerShell(string script, IReadOnlyDictionary<string, string> environment)
    {
        // Redirected output uses the console's code page, which loses the marker character. A console window takes
        // Unicode, so the test asks for UTF-8 to see what a console would show.
        var utf8Script = "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n" + script;
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(utf8Script));

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", encodedScript })
        {
            startInfo.ArgumentList.Add(argument);
        }
        foreach (var pair in environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        return Run(startInfo, standardInput: null);
    }

    private static string PowerShellQuote(string text)
    {
        return text.Replace("'", "''");
    }

    private static bool BashHasPromptCommandArrays()
    {
        if (OperatingSystem.IsWindows() ||
            !File.Exists("/bin/bash"))
        {
            return false;
        }

        var startInfo = new ProcessStartInfo("/bin/bash")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("echo $(( BASH_VERSINFO[0] * 100 + BASH_VERSINFO[1] ))");

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return int.TryParse(output.Trim(), out var version) && version >= 501;
    }

    private Dictionary<string, string> Run(ProcessStartInfo startInfo, string? standardInput)
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

        _standardOutput = outputTask.Result;
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
