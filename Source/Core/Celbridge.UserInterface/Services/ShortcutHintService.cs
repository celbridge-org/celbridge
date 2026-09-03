using Celbridge.Platform;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.UserInterface.Services;

public class ShortcutHintService : IShortcutHintService
{
    private readonly IPlatformInfo _platformInfo;
    private readonly IStringLocalizer _stringLocalizer;

    public ShortcutHintService(IPlatformInfo platformInfo, IStringLocalizer stringLocalizer)
    {
        _platformInfo = platformInfo;
        _stringLocalizer = stringLocalizer;
    }

    public string GetText(EditIntent intent)
    {
        // Redo is the one verb whose Control form differs between platforms, so it names its own resource.
        if (intent == EditIntent.Redo
            && _platformInfo.TreatsCtrlYAsRedo)
        {
            return _stringLocalizer.GetString("Shortcut_RedoCtrlY");
        }

        return GetText($"Shortcut_{intent}");
    }

    public string GetText(string shortcutName)
    {
        var form = _platformInfo.CommandModifier == CommandModifierKey.Command
            ? "Command"
            : "Control";

        return _stringLocalizer.GetString(shortcutName + form);
    }
}
