using Celbridge.UserInterface.DragDrop;
using Windows.Foundation;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Covers where a dragged tab lands. A pointer marks an insertion slot among a strip's tabs. A tab dropped in a slot
/// of the strip the tab sits in moves to a new index.
/// </summary>
[TestFixture]
public class TabDragGestureTests
{
    // Three tabs, 100 pixels wide, side by side from x = 10.
    private static readonly IReadOnlyList<Rect> ThreeTabs =
    [
        new Rect(10, 0, 100, 30),
        new Rect(110, 0, 100, 30),
        new Rect(210, 0, 100, 30)
    ];

    [TestCase(0, 0)]
    [TestCase(59, 0)]
    [TestCase(60, 1)]
    [TestCase(159, 1)]
    [TestCase(160, 2)]
    [TestCase(260, 3)]
    [TestCase(500, 3)]
    public void GetInsertionSlot_CountsTheTabsWhoseCenterIsBeforeThePointer(double pointerX, int expected)
    {
        TabDragGesture.GetInsertionSlot(pointerX, ThreeTabs).Should().Be(expected);
    }

    // Scrolled to the end, a virtualizing strip has not laid out the first tabs. Scrolled to the start, the strip has not
    // laid out the last tabs. Either way a slot is an index into every tab.
    [TestCase(0, 2)]
    [TestCase(60, 3)]
    [TestCase(260, 5)]
    public void GetInsertionSlot_AfterTabsThatAreNotLaidOut_CountsThem(double pointerX, int expected)
    {
        var tabBounds = new[] { Rect.Empty, Rect.Empty }.Concat(ThreeTabs).ToList();

        TabDragGesture.GetInsertionSlot(pointerX, tabBounds).Should().Be(expected);
    }

    [Test]
    public void GetInsertionSlot_PastTheLastLaidOutTab_IsTheSlotAfterIt()
    {
        var tabBounds = ThreeTabs.Concat(new[] { Rect.Empty, Rect.Empty }).ToList();

        TabDragGesture.GetInsertionSlot(500, tabBounds).Should().Be(3);
    }

    [Test]
    public void GetInsertionSlot_InAnEmptyStrip_IsTheFirstSlot()
    {
        TabDragGesture.GetInsertionSlot(50, Array.Empty<Rect>()).Should().Be(0);
    }

    [TestCase(2, 0, 0)]
    [TestCase(0, 2, 1)]
    [TestCase(0, 3, 2)]
    [TestCase(1, 1, 1)]
    [TestCase(1, 2, 1)]
    public void GetTargetIndex_ShiftsASlotAfterTheDraggedTabDownByOne(int currentIndex, int slot, int expected)
    {
        TabDragGesture.GetTargetIndex(currentIndex, slot).Should().Be(expected);
    }
}
