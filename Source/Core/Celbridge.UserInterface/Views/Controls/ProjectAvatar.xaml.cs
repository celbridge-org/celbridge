namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// A tile carrying a project's initials in white on a fill colour derived from the project name, so that a
/// project is recognisable at a glance wherever it is listed.
/// </summary>
public sealed partial class ProjectAvatar : UserControl
{
    // Proportions of the tile size, so an avatar keeps its look at any size.
    private const double CornerRadiusRatio = 0.25;
    private const double FontSizeRatio = 0.45;

    private const double DefaultSize = 24.0;

    public static readonly DependencyProperty ProjectNameProperty = DependencyProperty.Register(
        nameof(ProjectName),
        typeof(string),
        typeof(ProjectAvatar),
        new PropertyMetadata(string.Empty, OnProjectNameChanged));

    /// <summary>
    /// The project name that the initials and the fill colour are derived from.
    /// </summary>
    public string ProjectName
    {
        get => (string)GetValue(ProjectNameProperty);
        set => SetValue(ProjectNameProperty, value);
    }

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size),
        typeof(double),
        typeof(ProjectAvatar),
        new PropertyMetadata(DefaultSize, OnSizeChanged));

    /// <summary>
    /// The width and height of the tile in pixels.
    /// </summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public ProjectAvatar()
    {
        this.InitializeComponent();

        ApplySize();
        ApplyAvatar();
    }

    private static void OnProjectNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var projectAvatar = d as ProjectAvatar;
        projectAvatar?.ApplyAvatar();
    }

    private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var projectAvatar = d as ProjectAvatar;
        projectAvatar?.ApplySize();
    }

    private void ApplySize()
    {
        AvatarTile.Width = Size;
        AvatarTile.Height = Size;
        AvatarTile.CornerRadius = new CornerRadius(Size * CornerRadiusRatio);

        InitialsText.FontSize = Size * FontSizeRatio;
    }

    private void ApplyAvatar()
    {
        var projectName = ProjectName ?? string.Empty;

        InitialsText.Text = ProjectAvatarPalette.GetInitials(projectName);

        var tileColorHex = ProjectAvatarPalette.GetTileColorHex(projectName);
        AvatarTile.Background = new SolidColorBrush(HexColor.Parse(tileColorHex));
    }
}
