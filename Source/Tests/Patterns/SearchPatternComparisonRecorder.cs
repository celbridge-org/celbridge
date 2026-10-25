using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Celbridge.Tests.Patterns;

/// <summary>
/// Records ripgrep's answers for the comparison rows into SearchPatternComparisonAnswers.cs, and ripgrep's Unicode
/// tables into RipgrepUnicodeTables.cs. It runs only ripgrep 15.2.0. Run it after changing a row:
/// dotnet test --filter "FullyQualifiedName~SearchPatternComparisonRecorder".
/// </summary>
[TestFixture]
[Explicit("Runs ripgrep and rewrites SearchPatternComparisonAnswers.cs and RipgrepUnicodeTables.cs.")]
public class SearchPatternComparisonRecorder
{
    private const string RequiredVersion = "15.2.0";
    private const string AnswersFileName = "SearchPatternComparisonAnswers.cs";
    private const string TablesFilePath = "../../Core/Celbridge.Patterns/SearchPatterns/RipgrepUnicodeTables.cs";
    private const int FoldBatchSize = 64;

    private sealed record RipgrepRun(int ExitCode, string Output, string Error);

    private string _folder = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _folder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(SearchPatternComparisonRecorder), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);

        var versionRun = RunRipgrep(["--version"]);
        var firstLine = versionRun.Output.Split('\n')[0];
        var version = firstLine.Split(' ').ElementAtOrDefault(1) ?? string.Empty;
        if (version != RequiredVersion)
        {
            Assert.Fail($"The recorder needs ripgrep {RequiredVersion}, and 'rg --version' reports '{firstLine.Trim()}'.");
        }
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Test]
    public void RecordAnswers()
    {
        var rows = SearchPatternComparisonCases.Rows
            .Concat(SearchPatternComparisonCases.Differences)
            .OrderBy(row => row.Name, StringComparer.Ordinal);

        var answers = new StringBuilder();
        foreach (var row in rows)
        {
            var answer = AskRipgrep(row);
            answers.Append($"        [\"{row.Name}\"] = ");
            if (answer.IsRefused)
            {
                answers.Append("SearchPatternAnswer.Refused,\n");
                continue;
            }

            var replaced = "null";
            if (answer.Replaced is not null)
            {
                replaced = $"\"{EscapeForCSharp(answer.Replaced)}\"";
            }
            answers.Append($"new(false, \"{answer.Matches}\", {replaced}),\n");
        }

        var source = new StringBuilder();
        source.Append("namespace Celbridge.Tests.Patterns;\n");
        source.Append('\n');
        source.Append("/// <summary>\n");
        source.Append("/// ripgrep's answers for the rows in SearchPatternComparisonCases. SearchPatternComparisonRecorder writes this file.\n");
        source.Append("/// </summary>\n");
        source.Append("public static class SearchPatternComparisonAnswers\n");
        source.Append("{\n");
        source.Append("    /// <summary>\n");
        source.Append("    /// The version of ripgrep that gave the answers.\n");
        source.Append("    /// </summary>\n");
        source.Append($"    public const string RipgrepVersion = \"{RequiredVersion}\";\n");
        source.Append('\n');
        source.Append("    /// <summary>\n");
        source.Append("    /// The hash of the rows at the time of recording. SearchPatternComparisonCases.Fingerprint gives the current\n");
        source.Append("    /// one.\n");
        source.Append("    /// </summary>\n");
        source.Append($"    public const string CasesFingerprint = \"{SearchPatternComparisonCases.Fingerprint()}\";\n");
        source.Append('\n');
        source.Append("    /// <summary>\n");
        source.Append("    /// ripgrep's answer for each row, by row name.\n");
        source.Append("    /// </summary>\n");
        source.Append("    public static IReadOnlyDictionary<string, SearchPatternAnswer> Answers { get; } = new Dictionary<string, SearchPatternAnswer>(StringComparer.Ordinal)\n");
        source.Append("    {\n");
        source.Append(answers);
        source.Append("    };\n");
        source.Append("}\n");

        var answersFile = Path.Combine(SourceFolder(), AnswersFileName);
        File.WriteAllText(answersFile, source.ToString(), new UTF8Encoding(false));
    }

    [Test]
    public void RecordUnicodeTables()
    {
        // One code point on each line. The file leaves out '\n', which ends the lines, and the surrogates.
        var codePoints = new List<int>();
        for (var codePoint = 0; codePoint <= 0x10FFFF; codePoint++)
        {
            if (codePoint != '\n'
                && (codePoint < 0xD800 || codePoint > 0xDFFF))
            {
                codePoints.Add(codePoint);
            }
        }

        var codePointsText = new StringBuilder();
        foreach (var codePoint in codePoints)
        {
            codePointsText.Append(char.ConvertFromUtf32(codePoint));
            codePointsText.Append('\n');
        }

        var codePointsFile = Path.Combine(_folder, "code_points.txt");
        File.WriteAllText(codePointsFile, codePointsText.ToString(), new UTF8Encoding(false));

        var wordCodePoints = MatchingCodePoints(@"^\w$", codePointsFile, codePoints);
        var wordRanges = ToRanges(wordCodePoints);

        var foldSets = RecordCaseFoldSets(codePointsFile, codePoints);

        var source = new StringBuilder();
        source.Append("namespace Celbridge.Patterns;\n");
        source.Append('\n');
        source.Append("/// <summary>\n");
        source.Append("/// The Unicode tables of ripgrep's regex dialect. SearchPatternComparisonRecorder writes this file from ripgrep's\n");
        source.Append("/// answers.\n");
        source.Append("/// </summary>\n");
        source.Append("internal static class RipgrepUnicodeTables\n");
        source.Append("{\n");
        source.Append("    /// <summary>\n");
        source.Append("    /// The version of ripgrep that gave the tables.\n");
        source.Append("    /// </summary>\n");
        source.Append($"    public const string RipgrepVersion = \"{RequiredVersion}\";\n");
        source.Append('\n');
        source.Append("    /// <summary>\n");
        source.Append("    /// Lists the code points of ripgrep's \\w, as pairs of a range's first and last code point.\n");
        source.Append("    /// </summary>\n");
        source.Append("    public static ReadOnlySpan<int> WordRanges =>\n");
        source.Append("    [\n");
        for (var index = 0; index < wordRanges.Count; index += 4)
        {
            var line = wordRanges
                .Skip(index)
                .Take(4)
                .Select(range => $"0x{range.First:X4}, 0x{range.Last:X4},");
            source.Append("        ");
            source.Append(string.Join(' ', line));
            source.Append('\n');
        }
        source.Append("    ];\n");
        source.Append('\n');
        source.Append("    /// <summary>\n");
        source.Append("    /// Lists the sets of code points that ripgrep's (?i) matches as one letter. Each set is a size, then that many\n");
        source.Append("    /// code points.\n");
        source.Append("    /// </summary>\n");
        source.Append("    public static ReadOnlySpan<int> CaseFoldSets =>\n");
        source.Append("    [\n");
        foreach (var set in foldSets)
        {
            var members = set.Select(member => $"0x{member:X4},");
            source.Append($"        {set.Count}, ");
            source.Append(string.Join(' ', members));
            source.Append('\n');
        }
        source.Append("    ];\n");
        source.Append("}\n");

        var tablesFile = Path.GetFullPath(Path.Combine(SourceFolder(), TablesFilePath));
        File.WriteAllText(tablesFile, source.ToString(), new UTF8Encoding(false));
    }

    private SearchPatternAnswer AskRipgrep(SearchPatternCase row)
    {
        var textFile = Path.Combine(_folder, "text.txt");
        File.WriteAllText(textFile, row.Text, new UTF8Encoding(false));

        var commonArguments = new List<string>
        {
            "--crlf",
            "--text",
            "--color",
            "never",
        };
        commonArguments.AddRange(row.FlagList);

        var searchArguments = new List<string>(commonArguments)
        {
            "--json",
            "-e",
            row.Pattern,
            textFile,
        };
        var search = RunRipgrep(searchArguments);
        if (search.ExitCode == 2)
        {
            return SearchPatternAnswer.Refused;
        }

        var matches = ReadMatches(search.Output, row.Text);

        // With --crlf, ripgrep prints '\r\n' after a replaced match that spans lines, even where the text has '\n'.
        // --crlf changes nothing else for a text of '\n' line endings, so the replacement runs leave it out.
        string? replaced = null;
        if (row.Replacement is not null)
        {
            var replaceArguments = new List<string>(commonArguments);
            if (!row.Text.Contains('\r'))
            {
                replaceArguments.Remove("--crlf");
            }

            replaceArguments.AddRange(
            [
                "--passthru",
                "--no-filename",
                "--no-line-number",
                "-r",
                row.Replacement,
                "-e",
                row.Pattern,
                textFile,
            ]);
            var replace = RunRipgrep(replaceArguments);
            if (replace.ExitCode > 1)
            {
                Assert.Fail($"ripgrep refused the replacement in row '{row.Name}': {replace.Error}");
            }

            replaced = replace.Output;
            if (!row.Text.EndsWith('\n')
                && replaced.EndsWith('\n'))
            {
                replaced = replaced.Substring(0, replaced.Length - 1);
            }
        }

        return new SearchPatternAnswer(false, matches, replaced);
    }

    // Reads ripgrep's JSON matches, and converts each match's UTF-8 byte offsets to UTF-16 offsets in the text.
    private static string ReadMatches(string jsonLines, string text)
    {
        var utf16Offsets = Utf16OffsetsOfBytes(text);

        var matches = new List<string>();
        foreach (var line in jsonLines.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var message = JsonDocument.Parse(line);
            var root = message.RootElement;
            if (root.GetProperty("type").GetString() != "match")
            {
                continue;
            }

            var data = root.GetProperty("data");
            var lineOffset = data.GetProperty("absolute_offset").GetInt32();
            foreach (var submatch in data.GetProperty("submatches").EnumerateArray())
            {
                var start = utf16Offsets[lineOffset + submatch.GetProperty("start").GetInt32()];
                var end = utf16Offsets[lineOffset + submatch.GetProperty("end").GetInt32()];
                matches.Add($"{start}:{end - start}");
            }
        }

        return string.Join(' ', matches);
    }

    private static int[] Utf16OffsetsOfBytes(string text)
    {
        var bytes = Encoding.UTF8.GetByteCount(text);
        var offsets = new int[bytes + 1];
        var byteIndex = 0;
        var utf16Index = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            for (var index = 0; index < rune.Utf8SequenceLength; index++)
            {
                offsets[byteIndex + index] = utf16Index;
            }
            byteIndex += rune.Utf8SequenceLength;
            utf16Index += rune.Utf16SequenceLength;
        }
        offsets[bytes] = text.Length;

        return offsets;
    }

    private List<int> MatchingCodePoints(string pattern, string codePointsFile, List<int> codePoints)
    {
        var run = RunRipgrep(
        [
            "--text",
            "--line-number",
            "--no-filename",
            "--color",
            "never",
            "-e",
            pattern,
            codePointsFile,
        ]);
        if (run.ExitCode > 1)
        {
            Assert.Fail($"ripgrep refused '{pattern}': {run.Error}");
        }

        return run.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => codePoints[int.Parse(line.Substring(0, line.IndexOf(':'))) - 1])
            .ToList();
    }

    // Asks ripgrep which code points (?i) matches with each code point that .NET's case mappings change, in batches.
    // A last search checks that ripgrep folds no other code point.
    private List<List<int>> RecordCaseFoldSets(string codePointsFile, List<int> codePoints)
    {
        var candidates = new SortedSet<int>();
        foreach (var codePoint in codePoints)
        {
            var rune = new System.Text.Rune(codePoint);
            var upper = System.Text.Rune.ToUpperInvariant(rune).Value;
            var lower = System.Text.Rune.ToLowerInvariant(rune).Value;
            if (upper != codePoint
                || lower != codePoint)
            {
                candidates.Add(codePoint);
                candidates.Add(upper);
                candidates.Add(lower);
            }
        }

        var parents = new Dictionary<int, int>();
        int Find(int codePoint)
        {
            while (parents.TryGetValue(codePoint, out var parent)
                && parent != codePoint)
            {
                codePoint = parent;
            }

            return codePoint;
        }

        void Join(int first, int second)
        {
            parents.TryAdd(first, first);
            parents.TryAdd(second, second);
            var firstRoot = Find(first);
            var secondRoot = Find(second);
            if (firstRoot != secondRoot)
            {
                parents[Math.Max(firstRoot, secondRoot)] = Math.Min(firstRoot, secondRoot);
            }
        }

        var candidateList = candidates.ToList();
        for (var batchStart = 0; batchStart < candidateList.Count; batchStart += FoldBatchSize)
        {
            var batch = candidateList
                .Skip(batchStart)
                .Take(FoldBatchSize)
                .ToList();
            var alternatives = batch.Select(codePoint => $"(\\x{{{codePoint:X}}})");
            var pattern = "(?i)^(?:" + string.Join('|', alternatives) + ")$";
            var groups = batch.Select((_, index) => $"${{{index + 1}}}");
            var replacement = string.Join(',', groups);

            var run = RunRipgrep(
            [
                "--text",
                "--line-number",
                "--no-filename",
                "--only-matching",
                "--color",
                "never",
                "-r",
                replacement,
                "-e",
                pattern,
                codePointsFile,
            ]);
            if (run.ExitCode > 1)
            {
                Assert.Fail($"ripgrep refused a case folding search: {run.Error}");
            }

            foreach (var line in run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var colonIndex = line.IndexOf(':');
                var codePoint = codePoints[int.Parse(line.Substring(0, colonIndex)) - 1];
                var fields = line.Substring(colonIndex + 1).Split(',');
                var groupIndex = Array.FindIndex(fields, field => field.Length > 0);
                Join(codePoint, batch[groupIndex]);
            }
        }

        var foldSets = parents.Keys
            .GroupBy(Find)
            .Select(group => group.OrderBy(member => member).ToList())
            .Where(set => set.Count > 1)
            .OrderBy(set => set[0])
            .ToList();

        var folded = foldSets
            .SelectMany(set => set)
            .ToHashSet();
        var unfolded = codePoints
            .Where(codePoint => !folded.Contains(codePoint))
            .ToList();
        var unfoldedClass = string.Concat(ToRanges(unfolded)
            .Select(range => $"\\x{{{range.First:X}}}-\\x{{{range.Last:X}}}"));
        var checkRun = RunRipgrep(
        [
            "--text",
            "--count",
            "--no-filename",
            "-e",
            $"(?i)^[{unfoldedClass}]$",
            codePointsFile,
        ]);
        var unfoldedMatches = int.Parse(checkRun.Output.Trim());
        if (unfoldedMatches != unfolded.Count)
        {
            Assert.Fail($"ripgrep folds a code point that .NET's case mappings leave alone. (?i) matched {unfoldedMatches} lines for {unfolded.Count} unfolded code points.");
        }

        return foldSets;
    }

    private static List<(int First, int Last)> ToRanges(IEnumerable<int> codePoints)
    {
        var ranges = new List<(int First, int Last)>();
        foreach (var codePoint in codePoints.OrderBy(codePoint => codePoint))
        {
            if (ranges.Count > 0
                && ranges[^1].Last + 1 == codePoint)
            {
                ranges[^1] = (ranges[^1].First, codePoint);
            }
            else
            {
                ranges.Add((codePoint, codePoint));
            }
        }

        return ranges;
    }

    // Runs ripgrep with no configuration file, and returns what ripgrep prints. A missing ripgrep fails the test.
    private static RipgrepRun RunRipgrep(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("rg")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        startInfo.Environment.Remove("RIPGREP_CONFIG_PATH");
        startInfo.ArgumentList.Add("--no-config");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("ripgrep could not start.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("ripgrep could not start. Install ripgrep 15.2.0 and put 'rg' on the PATH.", exception);
        }

        using (process)
        {
            var errorTask = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            var error = errorTask.Result;

            return new RipgrepRun(process.ExitCode, output, error);
        }
    }

    private static string EscapeForCSharp(string text)
    {
        var escaped = new StringBuilder();
        foreach (var character in text)
        {
            switch (character)
            {
                case '\\':
                    escaped.Append("\\\\");
                    break;

                case '"':
                    escaped.Append("\\\"");
                    break;

                case '\n':
                    escaped.Append("\\n");
                    break;

                case '\r':
                    escaped.Append("\\r");
                    break;

                case '\t':
                    escaped.Append("\\t");
                    break;

                default:
                    if (character < 0x20
                        || character > 0x7E)
                    {
                        escaped.Append($"\\u{(int)character:X4}");
                    }
                    else
                    {
                        escaped.Append(character);
                    }
                    break;
            }
        }

        return escaped.ToString();
    }

    private static string SourceFolder([CallerFilePath] string sourceFile = "")
    {
        return Path.GetDirectoryName(sourceFile)!;
    }
}
