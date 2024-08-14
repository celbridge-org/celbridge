namespace Celbridge.Resources.Views;

public class FontFamilyConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string fontFamilyKey && Application.Current.Resources.ContainsKey(fontFamilyKey))
        {
            var fontFamily = Application.Current.Resources[fontFamilyKey] as FontFamily;

            return fontFamily;
        }
        return null;
    }

    public object? ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
