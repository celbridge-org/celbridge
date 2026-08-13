using Celbridge.Workspace;

namespace Celbridge.UserInterface.Views.PanelIcons;

public sealed partial class BottomAreaAlignmentIcon : UserControl
{
    // The 16x16 grid the icon is drawn on: the Utility Panel runs from the left edge to the first gutter,
    // the Main area between the gutters, and the Side area from the second gutter to the right edge.
    private const double LeftEdge = 1;
    private const double UtilityPanelGutterPosition = 5;
    private const double SideAreaGutterPosition = 11;
    private const double RightEdge = 15;
    private const double BottomEdge = 15;
    private const double BottomAreaTop = 10;

    public static readonly DependencyProperty AlignmentProperty =
        DependencyProperty.Register(
            nameof(Alignment),
            typeof(BottomAreaAlignment),
            typeof(BottomAreaAlignmentIcon),
            new PropertyMetadata(BottomAreaAlignment.Center, OnAlignmentChanged));

    public BottomAreaAlignment Alignment
    {
        get => (BottomAreaAlignment)GetValue(AlignmentProperty);
        set => SetValue(AlignmentProperty, value);
    }

    public BottomAreaAlignmentIcon()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Apply the current Alignment state when the control loads
        UpdateGeometry(Alignment);
        Loaded -= OnLoaded;
    }

    private static void OnAlignmentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var icon = (BottomAreaAlignmentIcon)d;
        var alignment = (BottomAreaAlignment)e.NewValue;
        icon.UpdateGeometry(alignment);
    }

    private void UpdateGeometry(BottomAreaAlignment alignment)
    {
        // Check if elements exist before updating (they may not exist if control hasn't loaded yet)
        if (BottomAreaBar is null ||
            UtilityPanelGutter is null ||
            SideAreaGutter is null)
        {
            return;
        }

        bool spansUtilityPanel = alignment == BottomAreaAlignment.Left ||
            alignment == BottomAreaAlignment.Justify;
        bool spansSideArea = alignment == BottomAreaAlignment.Right ||
            alignment == BottomAreaAlignment.Justify;

        double barLeft = spansUtilityPanel ? LeftEdge : UtilityPanelGutterPosition;
        double barRight = spansSideArea ? RightEdge : SideAreaGutterPosition;

        BottomAreaBar.Margin = new Thickness(barLeft, 0, 0, 1);
        BottomAreaBar.Width = barRight - barLeft;

        UtilityPanelGutter.Y2 = spansUtilityPanel ? BottomAreaTop : BottomEdge;
        SideAreaGutter.Y2 = spansSideArea ? BottomAreaTop : BottomEdge;
    }
}
