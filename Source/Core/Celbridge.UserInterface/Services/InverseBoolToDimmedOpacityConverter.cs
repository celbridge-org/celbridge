using Celbridge.UserInterface.Helpers;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Maps a boolean state to an opacity: true renders dimmed (still visible), false renders fully opaque.
/// The inverse of BoolToDimmedOpacityConverter, for a source property that names the set-apart state
/// rather than the ordinary one.
/// </summary>
public class InverseBoolToDimmedOpacityConverter : IValueConverter
{
    private const double EnabledOpacity = 1.0;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var boolValue = (bool)value;
        return boolValue ? Opacities.Dimmed : EnabledOpacity;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
