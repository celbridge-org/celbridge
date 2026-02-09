namespace Celbridge.Explorer.Views;

/// <summary>
/// A control that displays the appropriate icon for a file or folder resource.
/// </summary>
public sealed partial class ResourceIcon : UserControl
{
    /// <summary>
    /// The resource to display an icon for.
    /// </summary>
    public static readonly DependencyProperty ResourceProperty = DependencyProperty.Register(
        nameof(Resource),
        typeof(object),
        typeof(ResourceIcon),
        new PropertyMetadata(null, OnResourceChanged));

    public ResourceIcon()
    {
        this.InitializeComponent();
    }

    /// <summary>
    /// Gets or sets the resource. Can be IFolderResource or IFileResource.
    /// </summary>
    public object? Resource
    {
        get => GetValue(ResourceProperty);
        set => SetValue(ResourceProperty, value);
    }

    private static void OnResourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ResourceIcon resourceIcon)
        {
            resourceIcon.UpdateIconVisibility();
        }
    }

    private void UpdateIconVisibility()
    {
        if (Resource is IFolderResource)
        {
            // Show folder icon, hide file icon
            FolderIcon.Visibility = Visibility.Visible;
            FileIconControl.Visibility = Visibility.Collapsed;
        }
        else if (Resource is IFileResource fileResource)
        {
            // Show file icon with the correct icon definition, hide folder icon
            FolderIcon.Visibility = Visibility.Collapsed;
            FileIconControl.Visibility = Visibility.Visible;
            FileIconControl.IconDefinition = fileResource.Icon;
        }
        else
        {
            // Unknown resource type - hide both
            FolderIcon.Visibility = Visibility.Collapsed;
            FileIconControl.Visibility = Visibility.Collapsed;
        }
    }
}
