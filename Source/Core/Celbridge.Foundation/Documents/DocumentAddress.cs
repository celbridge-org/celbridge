namespace Celbridge.Documents;

/// <summary>
/// Identifies where a document tab is situated in the UI hierarchy. The area is derived from the section
/// rather than carried here.
/// </summary>
public record DocumentAddress(int WindowIndex, DocumentSection Section, int TabOrder)
{
    /// <summary>
    /// The TabOrder that places a tab at the end of its section rather than at a fixed position, for a
    /// caller naming the section a document opens in but not where in the tab row it lands.
    /// </summary>
    public const int AppendTabOrder = -1;
}
