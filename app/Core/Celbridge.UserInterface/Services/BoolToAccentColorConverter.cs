namespace Celbridge.UserInterface.Services;

/// <summary>
/// Converts a boolean value to an accent color brush (true = semi-transparent accent, false = transparent).
/// </summary>
public class BoolToAccentColorConverter : IValueConverter
{
    private static readonly SolidColorBrush TransparentBrush = new(Microsoft.UI.Colors.Transparent);

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var boolValue = value is bool b && b;

        if (!boolValue)
        {
            return TransparentBrush;
        }

        // Return a semi-transparent accent color for the highlighted state
        if (!Application.Current.Resources.TryGetValue("SystemAccentColorLight2", out var accentColor) ||
            accentColor is not Windows.UI.Color color)
        {
            throw new InvalidOperationException("Could not retrieve SystemAccentColorLight2 from application resources.");
        }

        return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(80, color.R, color.G, color.B));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
