namespace Celbridge.Tests.Utilities;

/// <summary>
/// Compares GitignoreMatcher with git's recorded answers. GitignoreComparisonRecorder records them from git
/// check-ignore. For each list in GitignoreComparisonCases, every path in the tree must get git's answer.
/// </summary>
[TestFixture]
public class GitignoreMatcherGitComparisonTests
{
    private const string RecordAgain =
        "The tree or a list changed since git's answers were recorded. Run GitignoreComparisonRecorder to record them again.";

    public static IEnumerable<string> ListNames()
    {
        return GitignoreComparisonCases.Lists.Keys;
    }

    public static IEnumerable<string> DifferenceListNames()
    {
        return GitignoreComparisonCases.DifferenceLists.Keys;
    }

    [Test]
    public void RecordedAnswers_AreForTheCurrentCases()
    {
        GitignoreComparisonAnswers.CasesFingerprint.Should().Be(GitignoreComparisonCases.Fingerprint(), RecordAgain);
    }

    [TestCaseSource(nameof(ListNames))]
    public void GitignoreList_MatchesGit(string listName)
    {
        var lines = GitignoreComparisonCases.Lists[listName];

        var differences = FindDifferences(listName, lines);

        differences.Should().BeEmpty();
    }

    [TestCaseSource(nameof(DifferenceListNames))]
    public void ListOutsideAscii_DiffersFromGitOnlyAtTheNonAsciiName(string listName)
    {
        var lines = GitignoreComparisonCases.DifferenceLists[listName];

        var differences = FindDifferences(listName, lines);

        differences.Should().Equal("café.txt: git keeps it, the matcher matches it");
    }

    private static List<string> FindDifferences(string listName, IReadOnlyList<string> lines)
    {
        var compileResult = GitignoreMatcher.Compile(lines, ignoreCase: false);
        compileResult.IsSuccess.Should().BeTrue(compileResult.FirstErrorMessage);
        var matcher = compileResult.Value;

        GitignoreComparisonAnswers.IgnoredPaths.TryGetValue(listName, out var ignoredByGit).Should().BeTrue(RecordAgain);
        var ignoredEntries = ignoredByGit!.ToHashSet(StringComparer.Ordinal);

        var differences = new List<string>();
        foreach (var entry in GitignoreComparisonCases.Entries)
        {
            var isIgnoredByGit = ignoredEntries.Contains(entry.Listed);
            var isMatched = matcher.IsMatch(entry.Path, entry.IsFolder);
            if (isMatched != isIgnoredByGit)
            {
                differences.Add($"{entry.Listed}: git {(isIgnoredByGit ? "ignores" : "keeps")} it, the matcher {(isMatched ? "matches" : "does not match")} it");
            }
        }

        return differences;
    }
}
