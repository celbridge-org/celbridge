using Celbridge.UserInterface;

namespace Celbridge.Explorer.Views;

/// <summary>
/// A control that displays the appropriate icon for a file or folder resource.
/// </summary>
public sealed partial class ResourceIcon : UserControl
{
    /// <summary>
    /// Identifies the IsFolder property. A folder shows the folder icon, and a file shows FileIcon.
    /// </summary>
    public static readonly DependencyProperty IsFolderProperty = DependencyProperty.Register(
        nameof(IsFolder),
        typeof(bool),
        typeof(ResourceIcon),
        new PropertyMetadata(false, OnIconChanged));

    /// <summary>
    /// Identifies the FileIcon property, the icon shown for a file.
    /// </summary>
    public static readonly DependencyProperty FileIconProperty = DependencyProperty.Register(
        nameof(FileIcon),
        typeof(IconDefinition),
        typeof(ResourceIcon),
        new PropertyMetadata(null, OnIconChanged));

    public ResourceIcon()
    {
        this.InitializeComponent();
    }

    public bool IsFolder
    {
        get => (bool)GetValue(IsFolderProperty);
        set => SetValue(IsFolderProperty, value);
    }

    public IconDefinition? FileIcon
    {
        get => (IconDefinition?)GetValue(FileIconProperty);
        set => SetValue(FileIconProperty, value);
    }

    private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ResourceIcon resourceIcon)
        {
            resourceIcon.UpdateIconVisibility();
        }
    }

    private void UpdateIconVisibility()
    {
        if (IsFolder)
        {
            FolderIcon.Visibility = Visibility.Visible;
            FileIconControl.Visibility = Visibility.Collapsed;
        }
        else if (FileIcon is not null)
        {
            FolderIcon.Visibility = Visibility.Collapsed;
            FileIconControl.Visibility = Visibility.Visible;
            FileIconControl.IconDefinition = FileIcon;
        }
        else
        {
            FolderIcon.Visibility = Visibility.Collapsed;
            FileIconControl.Visibility = Visibility.Collapsed;
        }
    }
}
