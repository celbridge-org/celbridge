using System.Xml.Linq;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Helper for gathering localized strings from .NET resources for WebView editors.
/// </summary>
public static class WebViewLocalizationHelper
{
    /// <summary>
    /// Gathers localized strings matching a key prefix.
    /// Reads the resource keys from the embedded .resw file and resolves their values
    /// through the provided string localizer.
    /// </summary>
    public static Dictionary<string, string> GetLocalizedStrings(IStringLocalizer stringLocalizer, string keyPrefix)
    {
        var assembly = typeof(WebViewLocalizationHelper).Assembly;
        using var stream = assembly.GetManifestResourceStream("Celbridge.Strings.Resources.resw");
        Guard.IsNotNull(stream);

        var reswDoc = XDocument.Load(stream);
        var strings = new Dictionary<string, string>();

        foreach (var data in reswDoc.Descendants("data"))
        {
            var name = data.Attribute("name")?.Value;
            if (name is not null && name.StartsWith(keyPrefix))
            {
                strings[name] = stringLocalizer.GetString(name);
            }
        }

        return strings;
    }
}
