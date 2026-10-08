using Celbridge.Host;
using Celbridge.Messaging;
using Celbridge.Workspace;
using StreamJsonRpc;

namespace Celbridge.Documents.Views;

/// <summary>
/// Handles IHostUtility RPC methods for a custom editor: passes the indicator a utility sets on itself to its
/// rail button. Document editors have no rail button, so the call is refused for them.
/// </summary>
public sealed class CustomUtilityIndicatorHandler : IHostUtility
{
    private const int MaxLabelLength = 40;

    private readonly IMessengerService _messengerService;
    private readonly EditorId _utilityId;
    private readonly bool _isUtility;

    private UtilityIndicatorTone _tone = UtilityIndicatorTone.None;
    private string _label = string.Empty;
    private string _iconName = string.Empty;

    public CustomUtilityIndicatorHandler(IMessengerService messengerService, EditorId utilityId, bool isUtility)
    {
        _messengerService = messengerService;
        _utilityId = utilityId;
        _isUtility = isUtility;
    }

    public void SetIndicator(string? tone = null, string? label = null, string? icon = null)
    {
        if (!_isUtility)
        {
            throw new LocalRpcException("Only a utility editor has a rail button to mark");
        }

        if (!UtilityIndicatorTones.TryParse(tone, out var parsedTone))
        {
            throw new LocalRpcException($"Unknown indicator tone '{tone}'. Use none, danger, caution, success or accent.");
        }

        var trimmedLabel = (label ?? string.Empty).Trim();
        if (trimmedLabel.Length > MaxLabelLength)
        {
            trimmedLabel = trimmedLabel[..MaxLabelLength];
        }
        var iconName = (icon ?? string.Empty).Trim();
        if (iconName.Length > 0 &&
            !IsPrefixedIconName(iconName))
        {
            throw new LocalRpcException($"Icon '{icon}' is not a prefixed icon name such as 'bs-record-circle-fill'.");
        }

        if (parsedTone == UtilityIndicatorTone.None)
        {
            trimmedLabel = string.Empty;
            iconName = string.Empty;
        }

        Apply(parsedTone, trimmedLabel, iconName);
    }

    /// <summary>
    /// Clears the indicator, when the page that set it reloads or the editor is torn down. A state the page
    /// no longer reports must not stay on the button.
    /// </summary>
    public void Reset()
    {
        Apply(UtilityIndicatorTone.None, string.Empty, string.Empty);
    }

    // "<font>-<name>", as every icon name in a manifest is. The icon control falls back to a default glyph for
    // a name it cannot resolve, so only the shape is checked here.
    private static bool IsPrefixedIconName(string iconName)
    {
        var dash = iconName.IndexOf('-');
        return dash > 0 &&
            dash < iconName.Length - 1 &&
            iconName.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    }

    private void Apply(UtilityIndicatorTone tone, string label, string iconName)
    {
        if (tone == _tone &&
            label == _label &&
            iconName == _iconName)
        {
            return;
        }

        _tone = tone;
        _label = label;
        _iconName = iconName;
        _messengerService.Send(new UtilityIndicatorChangedMessage(_utilityId, tone, label, iconName));
    }
}
