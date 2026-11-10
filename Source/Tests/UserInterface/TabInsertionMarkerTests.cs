using Celbridge.UserInterface.DragDrop;
using Windows.Foundation;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Covers where the insertion marker draws for a slot among a strip's tabs.
/// </summary>
[TestFixture]
public class TabInsertionMarkerTests
{
    private static readonly Rect Strip = new(0, 0, 400, 30);

    // Three tabs, 100 pixels wide, side by side from x = 10.
    private static readonly IReadOnlyList<Rect> ThreeTabs =
    [
        new Rect(10, 0, 100, 30),
        new Rect(110, 0, 100, 30),
        new Rect(210, 0, 100, 30)
    ];

    [TestCase(0, 10)]
    [TestCase(2, 210)]
    [TestCase(3, 310)]
    public void GetMarkerX_MarksTheLeadingEdgeOfTheSlotsTab_OrTheEndOfTheStrip(int slot, double expected)
    {
        TabInsertionMarker.GetMarkerX(Strip, ThreeTabs, slot, draggedIndex: -1).Should().Be(expected);
    }

    [TestCase(1)]
    [TestCase(2)]
    public void GetMarkerX_ForASlotEitherSideOfTheDraggedTab_MarksTheDraggedTab(int slot)
    {
        TabInsertionMarker.GetMarkerX(Strip, ThreeTabs, slot, draggedIndex: 1).Should().Be(110);
    }

    [Test]
    public void GetMarkerX_InAnEmptyStrip_MarksTheStart()
    {
        TabInsertionMarker.GetMarkerX(Strip, Array.Empty<Rect>(), 0, draggedIndex: -1).Should().Be(0);
    }

    [Test]
    public void GetMarkerX_BeforeTabsThatAreNotLaidOut_MarksTheEndOfTheLastLaidOutTab()
    {
        var tabBounds = ThreeTabs.Concat(new[] { Rect.Empty, Rect.Empty }).ToList();

        TabInsertionMarker.GetMarkerX(Strip, tabBounds, 3, draggedIndex: -1).Should().Be(310);
    }
}
