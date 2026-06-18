namespace Celbridge.UserInterface.Converters;

/// <summary>
/// Maps a StatusSeverity to the InfoBarSeverity used by InfoBar controls.
/// </summary>
public class StatusSeverityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value switch
        {
            StatusSeverity.Success => InfoBarSeverity.Success,
            StatusSeverity.Warning => InfoBarSeverity.Warning,
            StatusSeverity.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
