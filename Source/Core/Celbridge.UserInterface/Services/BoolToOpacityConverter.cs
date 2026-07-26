namespace Celbridge.UserInterface.Services;

/// <summary>
/// Maps a boolean state to an opacity: true renders fully opaque, false fades fully transparent. Suits a
/// glyph that shows and hides via opacity, such as a transient saving indicator. Distinct from
/// BoolToDimmedOpacityConverter, which keeps a false value visible but dimmed.
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
    private const double VisibleOpacity = 1.0;
    private const double HiddenOpacity = 0.0;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var boolValue = (bool)value;
        return boolValue ? VisibleOpacity : HiddenOpacity;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
