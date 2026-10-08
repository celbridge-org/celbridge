namespace Celbridge.UserInterface.Converters;

/// <summary>
/// Converts a boolean expanded state to the accessible name of a folder's expander button.
/// </summary>
public class ExpanderNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        var key = value is true ? "ResourceTree_CollapseFolder" : "ResourceTree_ExpandFolder";
        string name = stringLocalizer.GetString(key);
        return name;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
