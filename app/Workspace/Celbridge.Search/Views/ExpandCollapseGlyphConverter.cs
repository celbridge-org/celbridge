namespace Celbridge.Search.Views;

/// <summary>
/// Converts a boolean expanded state to the appropriate chevron glyph.
/// </summary>
public class ExpandCollapseGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isExpanded)
        {
            // Down chevron when expanded, right chevron when collapsed
            return isExpanded ? "\uE70D" : "\uE76C";
        }
        return "\uE76C";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
