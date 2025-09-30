namespace Celbridge.Localization;

/// <summary>
/// Lookup localized strings based on a resource name.
/// This is a simple wrapper for Microsoft.Extensions.Localization.
/// </summary>
public interface ILocalizerService
{
    /// <summary>
    /// Gets the string resource with the given name.
    /// </summary>
    string GetString(string name);

    /// <summary>
    /// Gets the string resource with the given name and formatted with the supplied arguments.
    /// </summary>
    string GetString(string name, params object[] arguments);
}
