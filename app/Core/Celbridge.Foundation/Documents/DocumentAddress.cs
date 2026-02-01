namespace Celbridge.Documents;

/// <summary>
/// Identifies where a document tab is situated in the UI hierarchy.
/// </summary>
public record DocumentAddress(int WindowIndex, int SectionIndex, int TabOrder);
