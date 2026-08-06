using Celbridge.Utilities;

namespace Celbridge.Tests.Utilities;

[TestFixture]
public class ResourceNameHelperTests
{
    [Test]
    public void GetNameSelectionRange_SelectsTheBasenameWithoutTheExtension()
    {
        var selectionRange = ResourceNameHelper.GetNameSelectionRange("report.md");

        selectionRange.Should().Be(0..6);
    }

    [Test]
    public void GetNameSelectionRange_SelectsTheWholeNameWhenThereIsNoExtension()
    {
        var selectionRange = ResourceNameHelper.GetNameSelectionRange("Documents");

        selectionRange.Should().Be(..);
    }

    [Test]
    public void GetNameSelectionRange_SelectsTheWholeNameOfADotfile()
    {
        // A leading dot starts the basename rather than an extension, so there is nothing to preserve.
        var selectionRange = ResourceNameHelper.GetNameSelectionRange(".gitignore");

        selectionRange.Should().Be(..);
    }

    [Test]
    public void GetNameSelectionRange_SelectsUpToTheLastDot()
    {
        var selectionRange = ResourceNameHelper.GetNameSelectionRange("archive.tar.gz");

        selectionRange.Should().Be(0..11);
    }
}
