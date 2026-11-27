using Celbridge.UserInterface.Helpers;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Covers how marked text splits a line around its mark. The resource picker uses marked text for a search result's
/// path, and the Search panel uses it for a matched line.
/// </summary>
[TestFixture]
public class MarkedTextTests
{
    [Test]
    public void AMark_SplitsTheTextIntoThreeParts()
    {
        MarkedText.Create("docs/Photo.png", 5, 5).Should().Be(new MarkedText("docs/", "Photo", ".png"));
    }

    [Test]
    public void AMarkAtEitherEnd_LeavesThatSideEmpty()
    {
        MarkedText.Create("notes.md", 0, 5).Should().Be(new MarkedText(string.Empty, "notes", ".md"));
        MarkedText.Create("notes.md", 5, 3).Should().Be(new MarkedText("notes", ".md", string.Empty));
    }

    [Test]
    public void UnmarkedText_IsAllBeforeTheMark()
    {
        MarkedText.Unmarked("notes.md").Should().Be(new MarkedText("notes.md", string.Empty, string.Empty));
    }

    [Test]
    public void AMarkThatRunsPastTheEnd_Throws()
    {
        var create = () => MarkedText.Create("notes.md", 6, 5);

        create.Should().Throw<ArgumentOutOfRangeException>();
    }
}
