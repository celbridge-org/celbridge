namespace Celbridge.Documents;

/// <summary>
/// The documents service provides functionality to support the documents panel in the workspace UI.
/// </summary>
public interface IDocumentsService
{
    /// <summary>
    /// Factory method to create the documents panel for the workspace UI.
    /// </summary>
    object CreateDocumentsPanel();
}
