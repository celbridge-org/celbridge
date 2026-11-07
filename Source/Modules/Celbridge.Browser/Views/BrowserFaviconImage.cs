using Celbridge.Platform;
using Celbridge.UserInterface.Views.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Celbridge.Browser.Views;

/// <summary>
/// A site's icon at the size of a small glyph. The control shows a default globe when it has no image bytes, and when
/// the image fails to decode.
/// </summary>
public sealed class BrowserFaviconImage : UserControl
{
    private const double IconSize = 16;
    private const string DefaultIconName = "bs-globe";
    private const string ImageAutomationId = "BrowserFaviconImage";

    public static readonly DependencyProperty ImageBytesProperty = DependencyProperty.Register(
        nameof(ImageBytes),
        typeof(byte[]),
        typeof(BrowserFaviconImage),
        new PropertyMetadata(null, OnImageBytesChanged));

    private readonly Icon _defaultIcon;
    private readonly Image _image;

    // Goes up with each new image, so a decode that finishes late cannot replace a newer image.
    private int _imageVersion;

    public BrowserFaviconImage()
    {
        _defaultIcon = new Icon
        {
            IconName = DefaultIconName,
            FontSize = IconSize,
            VerticalAlignment = VerticalAlignment.Center
        };

        _image = new Image
        {
            Width = IconSize,
            Height = IconSize,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };

        // The automation tools find this element only while it shows, which tells a drawn icon from the default globe.
        AutomationProperties.SetAutomationId(_image, ImageAutomationId);

        var root = new Grid
        {
            Width = IconSize,
            Height = IconSize
        };
        root.Children.Add(_defaultIcon);
        root.Children.Add(_image);

        Content = root;
    }

    public byte[]? ImageBytes
    {
        get => (byte[]?)GetValue(ImageBytesProperty);
        set => SetValue(ImageBytesProperty, value);
    }

    /// <summary>
    /// Creates the icon for a bookmark's item in the bookmark list.
    /// </summary>
    public static IconElement CreateMenuIcon(byte[]? imageBytes)
    {
        if (imageBytes is null)
        {
            return new Icon
            {
                IconName = DefaultIconName,
                FontSize = IconSize
            };
        }

        var icon = new ImageIcon
        {
            Width = IconSize,
            Height = IconSize
        };

        _ = LoadMenuIconAsync(icon, imageBytes);

        return icon;
    }

    private static async Task LoadMenuIconAsync(ImageIcon icon, byte[] imageBytes)
    {
        var bitmap = await DecodeAsync(imageBytes);
        if (bitmap is not null)
        {
            icon.Source = bitmap;
        }
    }

    private static void OnImageBytesChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var control = (BrowserFaviconImage)sender;
        _ = control.ShowImageAsync((byte[]?)e.NewValue);
    }

    private async Task ShowImageAsync(byte[]? imageBytes)
    {
        var version = ++_imageVersion;

        BitmapImage? bitmap = null;
        if (imageBytes is not null)
        {
            bitmap = await DecodeAsync(imageBytes);
        }

        if (version != _imageVersion)
        {
            return;
        }

        _image.Source = bitmap;
        _image.Visibility = bitmap is null ? Visibility.Collapsed : Visibility.Visible;
        _defaultIcon.Visibility = bitmap is null ? Visibility.Visible : Visibility.Collapsed;
    }

    // Returns null for an image the platform cannot decode, so the default icon stays.
    private static async Task<BitmapImage?> DecodeAsync(byte[] imageBytes)
    {
        var bitmap = new BitmapImage();

        try
        {
            var stream = new MemoryStream(imageBytes);
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());

            var platformInfo = ServiceLocator.AcquireService<IPlatformInfo>();
            if (!platformInfo.ThrowsForUndecodableImage
                && bitmap.PixelWidth <= 0)
            {
                return null;
            }

            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
