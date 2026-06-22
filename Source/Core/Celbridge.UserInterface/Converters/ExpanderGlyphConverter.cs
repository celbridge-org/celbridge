namespace Celbridge.UserInterface.Converters;

/// <summary>
/// Converts a boolean expanded state to the appropriate chevron glyph.
/// </summary>
public class ExpanderGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isExpanded)
        {
            // Down chevron when expanded, right chevron when collapsed
            return isExpanded ? IconSymbol.ChevronDown : IconSymbol.ChevronRight;
        }
        return IconSymbol.ChevronRight;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
