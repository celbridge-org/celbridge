namespace Celbridge.Console;

public enum ConsoleLogType
{
    Command,
    Info,
    Warning,
    Error,
}

public record ConsoleLogItem(ConsoleLogType LogType, string LogText, DateTime Timestamp)
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
                case ConsoleLogType.Command:
                    return "LightBlue";
                default:
                case ConsoleLogType.Info:
                    return "LightGreen";
                case ConsoleLogType.Warning:
                    return "Yellow";
                case ConsoleLogType.Error:
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
                case ConsoleLogType.Command:
                    return ChevronRightGlyph;
                default:
                case ConsoleLogType.Info:
                    return InfoGlyph;
                case ConsoleLogType.Warning:
                    return WarningGlyph;
                case ConsoleLogType.Error:
                    return ErrorGlyph;
            }
        }
    }
}