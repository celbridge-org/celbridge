namespace Celbridge.UserInterface.Services;

/// <summary>
/// Maps a boolean state to an opacity: true renders fully opaque, false renders dimmed (still visible). A
/// shared visual signal for an inactive item, such as a settings card whose feature is off. Distinct from
/// BoolToOpacityConverter, which fades a false value to fully transparent.
/// </summary>
public class BoolToDimmedOpacityConverter : IValueConverter
{
    private const double EnabledOpacity = 1.0;
    private const double DimmedOpacity = 0.5;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var boolValue = (bool)value;
        return boolValue ? EnabledOpacity : DimmedOpacity;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
