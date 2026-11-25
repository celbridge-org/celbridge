namespace Celbridge.Tests.Resources;

[TestFixture]
public class ResourceNameComparerTests
{
    [Test]
    public void NumbersSortByValue()
    {
        var names = new List<string>
        {
            "celbridge-v1.10",
            "celbridge-v1.9",
            "celbridge-v1.14",
            "celbridge-v1.8",
        };

        names.Sort(ResourceNameComparer.Instance);

        names.Should().Equal(
            "celbridge-v1.8",
            "celbridge-v1.9",
            "celbridge-v1.10",
            "celbridge-v1.14");
    }

    [Test]
    public void LetterCaseIsIgnored()
    {
        var names = new List<string>
        {
            "zebra.txt",
            "Apple.txt",
            "banana.txt",
            "README.md",
        };

        names.Sort(ResourceNameComparer.Instance);

        names.Should().Equal(
            "Apple.txt",
            "banana.txt",
            "README.md",
            "zebra.txt");
    }

    [Test]
    public void LongNumbersCompareByValue()
    {
        var comparer = ResourceNameComparer.Instance;

        comparer.Compare("file99999999999999999999.txt", "file100000000000000000000.txt").Should().BeNegative();
        comparer.Compare("page 2", "page 10").Should().BeNegative();
        comparer.Compare("2", "10").Should().BeNegative();
    }

    [Test]
    public void NonAsciiDigitsSortByValue()
    {
        // Fullwidth digits, as a Japanese input method types them.
        var names = new List<string>
        {
            "１５",
            "10",
            "２",
            "1",
        };

        names.Sort(ResourceNameComparer.Instance);

        names.Should().Equal(
            "1",
            "２",
            "10",
            "１５");
    }

    [Test]
    public void APrefixSortsFirst()
    {
        var comparer = ResourceNameComparer.Instance;

        comparer.Compare("file", "file.txt").Should().BeNegative();
        comparer.Compare("file1", "file1a").Should().BeNegative();
    }

    [Test]
    public void EqualNamesBreakTiesInOrdinalOrder()
    {
        var comparer = ResourceNameComparer.Instance;

        comparer.Compare("notes.md", "notes.md").Should().Be(0);
        comparer.Compare("Notes.md", "notes.md").Should().BeNegative();
        comparer.Compare("notes.md", "Notes.md").Should().BePositive();
        comparer.Compare("file01", "file1").Should().BeNegative();
    }
}
