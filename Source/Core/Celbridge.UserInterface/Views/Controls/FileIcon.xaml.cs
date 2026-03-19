namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// A user control that displays a file icon based on an icon name, file extension, or a direct IconDefinition binding.
/// </summary>
public sealed partial class FileIcon : UserControl
{
    private readonly IFileIconService _fileIconService;
    private bool _isIconDefinitionSetExternally;

    /// <summary>
    /// The icon source, which can be an icon name (e.g., "_file") or a file extension (e.g., ".cs" or "cs").
    /// </summary>
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source),
        typeof(string),
        typeof(FileIcon),
        new PropertyMetadata(string.Empty, OnSourceChanged));

    /// <summary>
    /// The size of the icon in pixels.
    /// </summary>
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size),
        typeof(double),
        typeof(FileIcon),
        new PropertyMetadata(16.0));

    /// <summary>
    /// The resolved icon definition based on the Source property, or set directly via binding.
    /// </summary>
    public static readonly DependencyProperty IconDefinitionProperty = DependencyProperty.Register(
        nameof(IconDefinition),
        typeof(FileIconDefinition),
        typeof(FileIcon),
        new PropertyMetadata(null, OnIconDefinitionChanged));

    public FileIcon()
    {
        _fileIconService = ServiceLocator.AcquireService<IFileIconService>();

        this.InitializeComponent();

        // Set default icon after InitializeComponent so bindings can override
        if (IconDefinition is null)
        {
            IconDefinition = _fileIconService.DefaultFileIcon;
        }
    }

    /// <summary>
    /// Gets or sets the icon source. Can be an icon name or file extension.
    /// </summary>
    public string Source
    {
        get => (string)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the icon size in pixels.
    /// </summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the icon definition. Can be set directly via binding or resolved from Source.
    /// </summary>
    public FileIconDefinition IconDefinition
    {
        get => (FileIconDefinition)GetValue(IconDefinitionProperty);
        set => SetValue(IconDefinitionProperty, value);
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FileIcon fileIcon)
        {
            fileIcon.UpdateIconDefinitionFromSource();
        }
    }

    private static void OnIconDefinitionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FileIcon fileIcon && e.NewValue is not null)
        {
            // Track that IconDefinition was set externally (not from Source)
            fileIcon._isIconDefinitionSetExternally = true;
        }
    }

    private void UpdateIconDefinitionFromSource()
    {
        // If Source is being used, it takes precedence
        if (!string.IsNullOrEmpty(Source))
        {
            _isIconDefinitionSetExternally = false;

            // Determine if Source is a file extension (starts with '.' or looks like an extension)
            if (Source.StartsWith('.') || 
                (Source.Length <= 10 && 
                !Source.StartsWith('_') && 
                Source.All(c => char.IsLetterOrDigit(c))))
            {
                var result = _fileIconService.GetFileIconForExtension(Source);
                IconDefinition = result.IsSuccess ? result.Value : _fileIconService.DefaultFileIcon;
            }
            else
            {
                // Treat as icon name
                var result = _fileIconService.GetFileIcon(Source);
                IconDefinition = result.IsSuccess ? result.Value : _fileIconService.DefaultFileIcon;
            }
        }
        else if (!_isIconDefinitionSetExternally)
        {
            // Only set default if IconDefinition wasn't set externally
            IconDefinition = _fileIconService.DefaultFileIcon;
        }
    }
}
