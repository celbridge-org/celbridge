using Celbridge.UserInterface.Helpers;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Maps a boolean state to an opacity: true renders fully opaque, false renders dimmed (still visible). A
/// shared visual signal for an inactive item, such as a settings card whose feature is off. Distinct from
/// BoolToOpacityConverter, which fades a false value to fully transparent. With ConverterParameter=Inverted,
/// true renders dimmed and false renders fully opaque.
/// </summary>
public class BoolToDimmedOpacityConverter : IValueConverter
{
    private const double EnabledOpacity = 1.0;

    enum Parameters
    {
        Normal, Inverted
    }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var boolValue = (bool)value;
        var direction = parameter == null ? Parameters.Normal : Enum.Parse<Parameters>((string)parameter);

        var isOpaque = direction == Parameters.Inverted ? !boolValue : boolValue;
        return isOpaque ? EnabledOpacity : Opacities.Dimmed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
