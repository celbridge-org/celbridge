using Celbridge.Commands;

namespace Celbridge.WorkspaceUI.Views;

/// <summary>
/// A reusable panel header control.
/// </summary>
public sealed partial class PanelHeader : UserControl
{
    private readonly ICommandService _commandService;

    /// <summary>
    /// The panel this header is associated with.
    /// </summary>
    public WorkspacePanel Panel
    {
        get => (WorkspacePanel)GetValue(PanelProperty);
        set => SetValue(PanelProperty, value);
    }

    public static readonly DependencyProperty PanelProperty =
        DependencyProperty.Register(
            nameof(Panel),
            typeof(WorkspacePanel),
            typeof(PanelHeader),
            new PropertyMetadata(WorkspacePanel.None, OnPanelChanged));

    private static void OnPanelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PanelHeader header)
        {
            header.FocusIndicatorControl.Panel = (WorkspacePanel)e.NewValue;
        }
    }

    /// <summary>
    /// The region used to collapse this panel when the close button is clicked.
    /// </summary>
    public LayoutRegion Region
    {
        get => (LayoutRegion)GetValue(RegionProperty);
        set => SetValue(RegionProperty, value);
    }

    public static readonly DependencyProperty RegionProperty =
        DependencyProperty.Register(
            nameof(Region),
            typeof(LayoutRegion),
            typeof(PanelHeader),
            new PropertyMetadata(LayoutRegion.None));

    /// <summary>
    /// The title text displayed in the header.
    /// </summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(PanelHeader),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Optional toolbar content to display in the header.
    /// </summary>
    public object? ToolbarContent
    {
        get => GetValue(ToolbarContentProperty);
        set => SetValue(ToolbarContentProperty, value);
    }

    public static readonly DependencyProperty ToolbarContentProperty =
        DependencyProperty.Register(
            nameof(ToolbarContent),
            typeof(object),
            typeof(PanelHeader),
            new PropertyMetadata(null));

    /// <summary>
    /// Whether the close button should be shown. Defaults to true.
    /// </summary>
    public bool ShowCloseButton
    {
        get => (bool)GetValue(ShowCloseButtonProperty);
        set => SetValue(ShowCloseButtonProperty, value);
    }

    public static readonly DependencyProperty ShowCloseButtonProperty =
        DependencyProperty.Register(
            nameof(ShowCloseButton),
            typeof(bool),
            typeof(PanelHeader),
            new PropertyMetadata(true, OnShowCloseButtonChanged));

    private static void OnShowCloseButtonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PanelHeader header)
        {
            header.CloseButton.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public PanelHeader()
    {
        _commandService = ServiceLocator.AcquireService<ICommandService>();

        this.InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (Region == LayoutRegion.None)
        {
            return;
        }

        _commandService.Execute<ISetRegionVisibilityCommand>(command =>
        {
            command.Regions = Region;
            command.IsVisible = false;
        });
    }

    private void TitleBar_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Region == LayoutRegion.None)
        {
            return;
        }

        // Double-clicking the title bar resets the panel to its default size
        _commandService.Execute<IResetPanelCommand>(command =>
        {
            command.Region = Region;
        });
    }
}
