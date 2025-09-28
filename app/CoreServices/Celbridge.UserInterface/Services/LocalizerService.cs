using Celbridge.Localization;

namespace Celbridge.UserInterface.Services;

public class LocalizerService : ILocalizerService
{
    private readonly IStringLocalizer _localizer;

    public LocalizerService(IStringLocalizer localizer)
    {
        _localizer = localizer;
    }

    public string GetString(string name)
    {
        try
        {
            return _localizer.GetString(name);
        }
        catch
        {
            // Return the string name if localized version not found
            return name;
        }
    }

    public string GetString(string name, params object[] arguments)
    {
        try
        {
            return _localizer.GetString(name, arguments);
        }
        catch
        {
            // Return the string name if localized version not found
            return name;
        }
    }
}
