namespace CelLegacy.Models;

[ResourceType("Text File", "A text file resource", "\uE8E9", ".txt,.md,.cs,.json,.xml")] // FontSize icon
public class TextFileResource : FileResource, IDocumentEntity
{
    public string Permissions { get; set; } = string.Empty;

    [PathProperty]
    public string SomePath { get; set; } = string.Empty;

    public static Result CreateResource(string path)
    {
        try
        {
            File.WriteAllText(path, string.Empty);
        }
        catch (Exception ex)
        {
            return new ErrorResult($"Failed to create file at '{path}'. {ex.Message}");
        }

        return new SuccessResult();
    }
}
