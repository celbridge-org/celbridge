using Celbridge.UserInterface.Helpers;

namespace Celbridge.Tests.UserInterface;

[TestFixture]
public class CardReorderTests
{
    private const double ListTop = 100;
    private const double RowPitch = 50;
    private const int RowCount = 4;

    private static CardReorderPlacement? Place(double pointerY, double grabOffset = 0)
    {
        var measurements = new CardReorderMeasurements(
            PointerY: pointerY,
            GrabOffset: grabOffset,
            ListTop: ListTop,
            RowPitch: RowPitch,
            RowCount: RowCount);

        return CardReorder.PlacementForPointer(measurements);
    }

    [Test]
    public void PlacementForPointer_AtRestingPosition_KeepsSlot()
    {
        var placement = Place(pointerY: ListTop + 2 * RowPitch);

        placement.Should().NotBeNull();
        placement!.SlotIndex.Should().Be(2);
        placement.Offset.Should().Be(0);
    }

    [Test]
    public void PlacementForPointer_PartwayBetweenSlots_HoldsRowUnderPointer()
    {
        // Two thirds of the way from slot 1 to slot 2: the row rounds into slot 2, and the offset holds it
        // where the pointer has it rather than snapping it to the slot.
        var placement = Place(pointerY: ListTop + RowPitch + 30);

        placement.Should().NotBeNull();
        placement!.SlotIndex.Should().Be(2);
        placement.Offset.Should().Be(-20);
    }

    [Test]
    public void PlacementForPointer_SubtractsGrabOffset()
    {
        // The pointer took hold 10px down the row, so the row's top is 10px above the pointer.
        var placement = Place(pointerY: ListTop + RowPitch + 10, grabOffset: 10);

        placement.Should().NotBeNull();
        placement!.SlotIndex.Should().Be(1);
        placement.Offset.Should().Be(0);
    }

    [Test]
    public void PlacementForPointer_AboveList_PinsToFirstSlot()
    {
        var placement = Place(pointerY: ListTop - 500);

        placement.Should().NotBeNull();
        placement!.SlotIndex.Should().Be(0);
        placement.Offset.Should().Be(0);
    }

    [Test]
    public void PlacementForPointer_BelowList_PinsToLastSlot()
    {
        var placement = Place(pointerY: ListTop + 5000);

        placement.Should().NotBeNull();
        placement!.SlotIndex.Should().Be(RowCount - 1);
        placement.Offset.Should().Be(0);
    }

    [Test]
    public void PlacementForPointer_UnmeasurableRows_ReturnsNull()
    {
        var measurements = new CardReorderMeasurements(
            PointerY: 500,
            GrabOffset: 0,
            ListTop: ListTop,
            RowPitch: 0,
            RowCount: RowCount);

        CardReorder.PlacementForPointer(measurements).Should().BeNull();
    }
}
