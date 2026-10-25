using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Celbridge.Tests.Patterns;

/// <summary>
/// Records git's answers for the comparison cases into GitignoreComparisonAnswers.cs. Run it after changing the tree
/// or a list: dotnet test --filter "FullyQualifiedName~GitignoreComparisonRecorder".
/// </summary>
[TestFixture]
[Explicit("Runs git and rewrites GitignoreComparisonAnswers.cs.")]
public class GitignoreComparisonRecorder
{
    private const string AnswersFileName = "GitignoreComparisonAnswers.cs";

    private string _repositoryFolder = string.Empty;
    private string _emptyConfigFile = string.Empty;

    [SetUp]
    public void SetUp()
    {
        var testFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(GitignoreComparisonRecorder), Guid.NewGuid().ToString("N"));
        _repositoryFolder = Path.Combine(testFolder, "repository");
        Directory.CreateDirectory(_repositoryFolder);

        // Git reads only an empty configuration file, so the machine's global excludes file leaves git's answers alone.
        _emptyConfigFile = Path.Combine(testFolder, "empty");
        File.WriteAllText(_emptyConfigFile, string.Empty);

        RunGit(["init", "--quiet"], standardInput: null);

        foreach (var entry in GitignoreComparisonCases.Entries)
        {
            var fullPath = Path.Combine(_repositoryFolder, entry.Path);
            if (entry.IsFolder)
            {
                Directory.CreateDirectory(fullPath);
            }
            else
            {
                File.WriteAllText(fullPath, string.Empty);
            }
        }
    }

    [TearDown]
    public void TearDown()
    {
        var testFolder = Path.GetDirectoryName(_repositoryFolder);
        if (!string.IsNullOrEmpty(testFolder)
            && Directory.Exists(testFolder))
        {
            Directory.Delete(testFolder, recursive: true);
        }
    }

    [Test]
    public void RecordGitAnswers()
    {
        var gitVersion = RunGit(["--version"], standardInput: null)
            .Trim()
            .Replace("git version ", string.Empty);

        var lists = GitignoreComparisonCases.Lists
            .Concat(GitignoreComparisonCases.DifferenceLists)
            .OrderBy(list => list.Key, StringComparer.Ordinal);

        var answers = new StringBuilder();
        foreach (var list in lists)
        {
            var ignoredPaths = AskGit(list.Value);
            var ignoredEntries = GitignoreComparisonCases.Entries
                .Where(entry => ignoredPaths.Contains(entry.Path))
                .ToList();

            if (ignoredEntries.Count == 0)
            {
                answers.Append($"        [\"{list.Key}\"] = [],\n");
                continue;
            }

            answers.Append($"        [\"{list.Key}\"] =\n");
            answers.Append("        [\n");
            foreach (var entry in ignoredEntries)
            {
                answers.Append($"            \"{EscapeForCSharp(entry.Listed)}\",\n");
            }
            answers.Append("        ],\n");
        }

        var source = new StringBuilder();
        source.Append("namespace Celbridge.Tests.Patterns;\n");
        source.Append('\n');
        source.Append("/// <summary>\n");
        source.Append("/// The paths git ignores for each list in GitignoreComparisonCases. GitignoreComparisonRecorder writes this file.\n");
        source.Append("/// </summary>\n");
        source.Append("public static class GitignoreComparisonAnswers\n");
        source.Append("{\n");
        source.Append("    /// <summary>\n");
        source.Append("    /// The version of git that gave the answers.\n");
        source.Append("    /// </summary>\n");
        source.Append($"    public const string GitVersion = \"{EscapeForCSharp(gitVersion)}\";\n");
        source.Append('\n');
        source.Append("    /// <summary>\n");
        source.Append("    /// The hash of the tree and the lists at the time of recording. GitignoreComparisonCases.Fingerprint gives the\n");
        source.Append("    /// current one.\n");
        source.Append("    /// </summary>\n");
        source.Append($"    public const string CasesFingerprint = \"{GitignoreComparisonCases.Fingerprint()}\";\n");
        source.Append('\n');
        source.Append("    /// <summary>\n");
        source.Append("    /// The paths git ignores, by list name, in the tree's order.\n");
        source.Append("    /// </summary>\n");
        source.Append("    public static IReadOnlyDictionary<string, IReadOnlyList<string>> IgnoredPaths { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)\n");
        source.Append("    {\n");
        source.Append(answers);
        source.Append("    };\n");
        source.Append("}\n");

        var answersFile = Path.Combine(SourceFolder(), AnswersFileName);
        File.WriteAllText(answersFile, source.ToString(), new UTF8Encoding(false));
    }

    private HashSet<string> AskGit(IReadOnlyList<string> lines)
    {
        var excludeFile = Path.Combine(_repositoryFolder, ".git", "info", "exclude");
        Directory.CreateDirectory(Path.GetDirectoryName(excludeFile)!);
        File.WriteAllText(excludeFile, string.Join("\n", lines) + "\n", new UTF8Encoding(false));

        var standardInput = new StringBuilder();
        foreach (var entry in GitignoreComparisonCases.Entries)
        {
            standardInput.Append(entry.Path);
            standardInput.Append('\0');
        }

        var output = RunGit(["check-ignore", "--no-index", "--stdin", "-z"], standardInput.ToString());

        return output
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    // Runs git in the repository folder, with only the repository's configuration and exact letter case, and returns
    // what git prints. A missing git or a git error fails the test. check-ignore exits with 1 when it keeps every path.
    private string RunGit(IReadOnlyList<string> arguments, string? standardInput)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = _repositoryFolder,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
        };

        var gitVariables = startInfo.Environment.Keys
            .Where(name => name.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var name in gitVariables)
        {
            startInfo.Environment.Remove(name);
        }
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = _emptyConfigFile;
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";

        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("core.ignorecase=false");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add($"core.excludesFile={_emptyConfigFile}");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("git could not start.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("git could not start.", exception);
        }

        using (process)
        {
            if (standardInput is not null)
            {
                process.StandardInput.Write(standardInput);
            }
            process.StandardInput.Close();

            var errorTask = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            var error = errorTask.Result;

            if (process.ExitCode != 0
                && process.ExitCode != 1)
            {
                Assert.Fail($"git {string.Join(' ', arguments)} exited with {process.ExitCode}: {error}");
            }

            return output;
        }
    }

    private static string EscapeForCSharp(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }

    private static string SourceFolder([CallerFilePath] string sourceFile = "")
    {
        return Path.GetDirectoryName(sourceFile)!;
    }
}
