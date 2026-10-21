using Celbridge.Resources.Helpers;

namespace Celbridge.Tests.Resources;

/// <summary>
/// A refusal for a key that differs from the names stored on disk gives the stored names. These tests pin how a
/// stored name is matched, and how a path keeps the parts that do not exist yet.
/// </summary>
[TestFixture]
public class StoredNameFinderTests
{
    private string _folderPath = null!;

    [SetUp]
    public void Setup()
    {
        var folderPath = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(StoredNameFinderTests));
        if (Directory.Exists(folderPath))
        {
            Directory.Delete(folderPath, true);
        }
        Directory.CreateDirectory(folderPath);

        _folderPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_folderPath))
        {
            Directory.Delete(_folderPath, true);
        }
    }

    [Test]
    [Platform(Exclude = "Linux", Reason = "On a case-sensitive volume, names that differ in case name different items.")]
    public void FindStoredPathGivesEachExistingPartItsStoredName()
    {
        WriteFile("Docs/Notes.txt");

        var storedPath = StoredNameFinder.FindStoredPath(_folderPath, Path.Combine("DOCS", "notes.TXT"));

        storedPath.Should().Be(Path.Combine(_folderPath, "Docs", "Notes.txt"));
    }

    [Test]
    [Platform(Exclude = "Linux", Reason = "On a case-sensitive volume, names that differ in case name different items.")]
    public void FindStoredPathKeepsTheMissingPartsAsGiven()
    {
        WriteFile("Docs/Notes.txt");

        var storedPath = StoredNameFinder.FindStoredPath(_folderPath, Path.Combine("docs", "New", "file.txt"));

        storedPath.Should().Be(Path.Combine(_folderPath, "Docs", "New", "file.txt"));
    }

    [Test]
    public void FindStoredNameReturnsAnExactMatch()
    {
        WriteFile("Notes.txt");

        StoredNameFinder.FindStoredName(_folderPath, "Notes.txt").Should().Be("Notes.txt");
    }

    [Test]
    public void FindStoredNameMatchesAnotherCaseAndUnicodeNormalization()
    {
        // The file's name is stored decomposed, and the name looked up is composed and in capitals.
        WriteFile("Café.txt");

        StoredNameFinder.FindStoredName(_folderPath, "CAFÉ.txt").Should().Be("Café.txt");
    }

    [Test]
    public void FindStoredNameReturnsNullForANameWithNoMatch()
    {
        WriteFile("Notes.txt");

        StoredNameFinder.FindStoredName(_folderPath, "Other.txt").Should().BeNull();
    }

    private void WriteFile(string relativePath)
    {
        var filePath = Path.Combine(_folderPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, "content");
    }
}
