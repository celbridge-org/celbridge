namespace Celbridge.Documents;

/// <summary>
/// Identifies where a document tab is situated in the UI hierarchy. The area is derived from the section
/// rather than carried here.
/// </summary>
public record DocumentAddress(int WindowIndex, DocumentSection Section, int TabOrder);
