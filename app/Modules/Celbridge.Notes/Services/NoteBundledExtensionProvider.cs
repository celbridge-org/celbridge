namespace Celbridge.Notes;

/// <summary>
/// Provides the bundled extension path for the Note editor.
/// The Note editor's assets (editor.json, JS, localization, templates) are
/// deployed as content to the Celbridge.Notes/Web/note/ directory.
/// </summary>
public class NoteBundledExtensionProvider : IBundledExtensionProvider
{
    public string GetExtensionDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "Celbridge.Notes", "Web", "note");
    }
}
