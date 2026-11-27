namespace Celbridge.UserInterface.ViewModels.Controls;

/// <summary>
/// Lists the file extensions a resource picker allows. An empty list allows every extension.
/// </summary>
internal sealed class ResourcePickerExtensions
{
    // Each extension without its dot, in lowercase.
    private readonly List<string> _extensions;

    /// <summary>
    /// Allows every extension.
    /// </summary>
    public static ResourcePickerExtensions Any { get; } = new([]);

    public ResourcePickerExtensions(IEnumerable<string> extensions)
    {
        _extensions = extensions
            .Select(extension => extension.TrimStart('.').ToLowerInvariant())
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Returns true when the picker allows every extension.
    /// </summary>
    public bool AllowsAny => _extensions.Count == 0;

    /// <summary>
    /// The extensions as file filters, for example "*.png, *.jpg".
    /// </summary>
    public string FilterText => string.Join(", ", _extensions.Select(extension => $"*.{extension}"));

    /// <summary>
    /// The extensions with their dots, for example ".png, .jpg".
    /// </summary>
    public string ListText => string.Join(", ", _extensions.Select(extension => $".{extension}"));

    /// <summary>
    /// Returns true if the name ends in an allowed extension, or when every extension is allowed.
    /// </summary>
    public bool IsAllowed(string name)
    {
        if (AllowsAny)
        {
            return true;
        }

        return _extensions.Any(extension => name.EndsWith($".{extension}", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// With one allowed extension, adds the extension to a name typed without it. Returns any other name unchanged.
    /// </summary>
    public string AddSingleExtension(string typedName)
    {
        if (_extensions.Count != 1
            || string.IsNullOrWhiteSpace(typedName))
        {
            return typedName;
        }

        var extension = $".{_extensions[0]}";
        if (typedName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            return typedName;
        }

        return typedName + extension;
    }
}
