namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// The measurements a card placement is calculated from: where the pointer is, where within the dragged
/// row it took hold, and the geometry of the list taken when the drag began.
/// </summary>
public sealed record CardReorderMeasurements(
    double PointerY,
    double GrabOffset,
    double ListTop,
    double RowPitch,
    int RowCount);

/// <summary>
/// Where a dragged card belongs: the slot it should occupy, and how far it sits from that slot's resting
/// position so it stays under the pointer.
/// </summary>
public sealed record CardReorderPlacement(int SlotIndex, double Offset);

/// <summary>
/// Places a dragged card in a list of uniformly sized rows.
/// </summary>
public static class CardReorder
{
    /// <summary>
    /// Returns the slot the dragged row should occupy for a pointer position, or null for a list too small
    /// to measure. The row is held inside the list, so dragging past either end pins it to the first or
    /// last slot rather than carrying it off the list. Every input is either the pointer or a measurement
    /// taken at the grab, never where the rows currently sit, so a row that has just moved cannot feed back
    /// into the next placement and set the list oscillating.
    /// </summary>
    public static CardReorderPlacement? PlacementForPointer(CardReorderMeasurements measurements)
    {
        if (measurements.RowPitch <= 0)
        {
            return null;
        }

        var lastSlotTop = measurements.ListTop + (measurements.RowCount - 1) * measurements.RowPitch;
        var pointerRowTop = measurements.PointerY - measurements.GrabOffset;
        var rowTop = Math.Max(measurements.ListTop, Math.Min(pointerRowTop, lastSlotTop));

        var slotOffset = (rowTop - measurements.ListTop) / measurements.RowPitch;
        var slotIndex = (int)Math.Round(slotOffset, MidpointRounding.AwayFromZero);

        var restingTop = measurements.ListTop + slotIndex * measurements.RowPitch;

        return new CardReorderPlacement(slotIndex, rowTop - restingTop);
    }
}
