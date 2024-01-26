using Microsoft.UI.Xaml.Data;

namespace CelLegacy.Utils;

public class BoolToVisibilityConverter : IValueConverter
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
            return !boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
