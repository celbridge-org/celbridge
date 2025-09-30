namespace Celbridge.Screenplay.Models;

public record Scene(string Category, string Namespace, string Context, string AssetPath, List<DialogueLine> Lines)
{
    public Scene(string Category, string Namespace, string Context, string AssetPath)
        : this(Category, Namespace, Context, AssetPath, new List<DialogueLine>())
    {}
}
