namespace CelLegacy.Models;

[AttributeUsage(AttributeTargets.Class)]
public class ResourceTypeAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }
    public string Icon { get; }
    public List<string> Extensions { get; } = new List<string>();

    public ResourceTypeAttribute(string name, string description, string iconPath, string extensions)
    {
        Name = name;
        Description = description;
        Icon = iconPath;

        if (name == "Folder")
        {
            return;
        }

        Guard.IsFalse(string.IsNullOrEmpty(extensions));

        var splitExtensions = extensions.Split(',');
        foreach (var extension in splitExtensions)
        {
            var trimmed = extension.Trim();
            if (!extension.StartsWith('.'))
            {
                throw new InvalidOperationException($"File extension for ResourceType '{Name}' must start with .");
            }
            Extensions.Add(extension);
        }
    }
}
