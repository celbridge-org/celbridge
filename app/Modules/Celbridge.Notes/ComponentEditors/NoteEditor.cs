using Celbridge.Entities;

namespace Celbridge.Notes.ComponentEditors;

public class NoteEditor : ComponentEditorBase
{
    private const string ConfigPath = "Celbridge.Notes.Assets.Components.NoteComponent.json";
    private const string ComponentFormPath = "Celbridge.Notes.Assets.Forms.NoteForm.json";

    public const string ComponentType = "Notes.Note";

    public NoteEditor()
    { }

    public override string GetComponentConfig()
    {
        return LoadEmbeddedResource(ConfigPath);
    }

    public override string GetComponentForm()
    {
        return LoadEmbeddedResource(ComponentFormPath);
    }

    public override ComponentSummary GetComponentSummary()
    {
        return new ComponentSummary(string.Empty, string.Empty);
    }
}
