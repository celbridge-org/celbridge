namespace Celbridge.Console.Models;

public record ConsoleLogItem(MessageType LogType, string LogText, DateTime Timestamp)
{
    private const string ChevronRightGlyph = "\ue76C";
    private const string InfoGlyph = "\ue946";
    private const string WarningGlyph = "\ue7BA";
    private const string ErrorGlyph = "\ue783";

    public string Color
    {
        get
        {
            switch (LogType)
            {
                case MessageType.Command:
                    return "LightBlue";
                default:
                case MessageType.Information:
                    return "LightGreen";
                case MessageType.Warning:
                    return "Yellow";
                case MessageType.Error:
                    return "Red";

            }
        }
    }

    public string Glyph
    {
        get
        {
            switch (LogType)
            {
                case MessageType.Command:
                    return ChevronRightGlyph;
                default:
                case MessageType.Information:
                    return InfoGlyph;
                case MessageType.Warning:
                    return WarningGlyph;
                case MessageType.Error:
                    return ErrorGlyph;
            }
        }
    }
}
