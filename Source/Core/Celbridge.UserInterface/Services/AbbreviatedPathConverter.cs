namespace Celbridge.UserInterface.Services;

/// <summary>
/// Shortens an absolute path for display by replacing the user's home folder with a tilde, so that a bound
/// path reads the same way as the ones the project menu presents.
/// </summary>
public class AbbreviatedPathConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var path = value as string ?? string.Empty;

        return DisplayPathFormatter.AbbreviateHomeFolder(path);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
