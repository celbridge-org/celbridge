namespace Celbridge.UserInterface.Services;

/// <summary>
/// Converts a boolean value to an opacity value.
/// By default, true returns 1 (fully opaque) and false returns 0 (fully transparent).
/// When the "Inverted" parameter is provided, the logic is reversed.
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
    enum Parameters
    {
        Normal, Inverted
    }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var boolValue = (bool)value;
        Parameters direction = parameter == null ? Parameters.Normal : (Parameters)Enum.Parse(typeof(Parameters), (string)parameter);

        if (direction == Parameters.Inverted)
        {
            return !boolValue ? 1.0 : 0.0;
        }

        return boolValue ? 1.0 : 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
