namespace Celbridge.Legacy.Models;

public abstract class FileResource : Resource
{
    // The latest known hash of the file contents. Used to determine if the file has been
    // modified while the application is running.
    [HideProperty]
    [JsonIgnore]
    public string Hash { get; set; } = string.Empty;
}
