using System.Globalization;
using System.Xml.Linq;
using Celbridge.Localization;
using Celbridge.Tests.Architecture;

namespace Celbridge.Tests.Localization;

/// <summary>
/// An ILocalizerService backed by the application's en-US resources, so a test asserts the wording a
/// user actually reads and a key with no entry fails rather than passing against a stub that echoes
/// whatever it is handed. Misses return the name, matching LocalizerService.
/// </summary>
internal sealed class TestLocalizerService : ILocalizerService
{
    private readonly IReadOnlyDictionary<string, string> _strings;

    public TestLocalizerService()
    {
        _strings = LoadStrings();
    }

    public string GetString(string name)
    {
        if (_strings.TryGetValue(name, out var value))
        {
            return value;
        }

        return name;
    }

    public string GetString(string name, params object[] arguments)
    {
        var template = GetString(name);

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, arguments);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    /// <summary>
    /// Reads every name and value declared in the application's en-US Resources.resw.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> LoadStrings()
    {
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        if (string.IsNullOrEmpty(sourceFolder))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var resourcesPath = Path.Combine(
            sourceFolder, "Celbridge", "Resources", "Strings", "en-US", "Resources.resw");

        if (!File.Exists(resourcesPath))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var document = XDocument.Load(resourcesPath);
        var root = document.Root;
        if (root is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in root.Elements("data"))
        {
            var name = element.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            strings[name] = element.Element("value")?.Value ?? string.Empty;
        }

        return strings;
    }
}
