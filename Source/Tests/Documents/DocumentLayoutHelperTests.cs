using Celbridge.Documents;
using Celbridge.Utilities;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers DocumentLayoutHelper: the area and section mapping, and the section an unaddressed open
/// lands in.
/// </summary>
[TestFixture]
public class DocumentLayoutHelperTests
{
    [Test]
    public void GetArea_MapsEverySectionToItsArea()
    {
        DocumentSection.MainLeft.GetArea().Should().Be(DocumentArea.Main);
        DocumentSection.MainRight.GetArea().Should().Be(DocumentArea.Main);
        DocumentSection.BottomLeft.GetArea().Should().Be(DocumentArea.Bottom);
        DocumentSection.BottomRight.GetArea().Should().Be(DocumentArea.Bottom);
        DocumentSection.SideTop.GetArea().Should().Be(DocumentArea.Side);
        DocumentSection.SideBottom.GetArea().Should().Be(DocumentArea.Side);
    }

    [Test]
    public void DefaultOpenSection_IsTheSectionThatIsAlwaysPresent()
    {
        var defaultSection = DocumentLayoutHelper.DefaultOpenSection;

        defaultSection.Should().Be(DocumentSection.MainLeft);

        // An unaddressed open must never land in a section that can be collapsed or unmounted.
        defaultSection.IsSecondarySection().Should().BeFalse();
        defaultSection.GetArea().IsCollapsible().Should().BeFalse();
    }
}
