namespace Celbridge.UserInterface.Converters;

/// <summary>
/// Converts a boolean to a FontWeight. True returns SemiBold, false returns Normal.
/// </summary>
public class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isTrue && isTrue)
        {
            return Microsoft.UI.Text.FontWeights.SemiBold;
        }
        return Microsoft.UI.Text.FontWeights.Normal;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
