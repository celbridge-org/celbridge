namespace Celbridge.UserInterface.Converters;

/// <summary>
/// Converts an integer count to a boolean value.
/// Returns true if count is greater than 0, false otherwise.
/// </summary>
public class CountToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int count)
        {
            return count > 0;
        }

        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
