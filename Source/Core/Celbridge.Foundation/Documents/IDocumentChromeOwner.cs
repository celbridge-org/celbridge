namespace Celbridge.Documents;

/// <summary>
/// A document view that can hide the chrome carrying its own controls, leaving the user no way to bring
/// it back. The document tab menu offers a restore action on the view's behalf, labelled by the view so
/// the shared menu never has to name a particular kind of chrome.
/// </summary>
public interface IDocumentChromeOwner
{
    /// <summary>
    /// True when the view's chrome is currently hidden and can be restored.
    /// </summary>
    bool CanRestoreChrome { get; }

    /// <summary>
    /// Localization key for the restore action's menu text.
    /// </summary>
    string RestoreChromeMenuTextKey { get; }

    /// <summary>
    /// Restores the hidden chrome, persisting the change to the document.
    /// </summary>
    void RestoreChrome();
}
