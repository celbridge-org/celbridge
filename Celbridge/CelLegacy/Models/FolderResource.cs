namespace Celbridge.Legacy.Models;

[ResourceType("Folder", "Contains file resources", "\uE188", "")] // Folder icon
public class FolderResource : Resource
{
    [JsonIgnore]
    public IEnumerable<FileResource> Files => Children.OfType<FileResource>();

    [JsonIgnore]
    public IEnumerable<FolderResource> Folders => Children.OfType<FolderResource>();

    public static Result CreateResource(string path)
    {
        if (Directory.Exists(path))
        {
            return new ErrorResult($"A file or folder already exists at '{path}'");
        }

        if (!FileUtils.IsAbsolutePathValid(path))
        {
            return new ErrorResult($"Invalid path '{path}'.");
        }

        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex)
        {
            return new ErrorResult($"Failed to create folder at '{path}'. {ex.Message}");
        }

        return new SuccessResult();
    }
}
