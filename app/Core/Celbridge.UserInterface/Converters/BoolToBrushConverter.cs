using Microsoft.UI.Xaml.Media;

namespace Celbridge.UserInterface.Converters;

/// <summary>
/// Converts a boolean to a Brush. True returns TrueBrush, false returns FalseBrush.
/// Both brushes can be configured via properties.
/// </summary>
public partial class BoolToBrushConverter : DependencyObject, IValueConverter
{
    public static readonly DependencyProperty TrueBrushProperty =
        DependencyProperty.Register(
            nameof(TrueBrush),
            typeof(Brush),
            typeof(BoolToBrushConverter),
            new PropertyMetadata(null));

    public static readonly DependencyProperty FalseBrushProperty =
        DependencyProperty.Register(
            nameof(FalseBrush),
            typeof(Brush),
            typeof(BoolToBrushConverter),
            new PropertyMetadata(null));

    public Brush? TrueBrush
    {
        get => (Brush?)GetValue(TrueBrushProperty);
        set => SetValue(TrueBrushProperty, value);
    }

    public Brush? FalseBrush
    {
        get => (Brush?)GetValue(FalseBrushProperty);
        set => SetValue(FalseBrushProperty, value);
    }

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isTrue)
        {
            return isTrue ? TrueBrush : FalseBrush;
        }
        return FalseBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
