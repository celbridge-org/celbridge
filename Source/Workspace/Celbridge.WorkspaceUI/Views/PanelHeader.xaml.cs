using Celbridge.Commands;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Microsoft.Extensions.Localization;

namespace Celbridge.WorkspaceUI.Views;

/// <summary>
/// A reusable panel header control.
/// </summary>
public sealed partial class PanelHeader : UserControl
{
    private readonly ICommandService _commandService;
    private readonly IStringLocalizer _stringLocalizer;

    private string CollapseButtonTooltip => _stringLocalizer.GetString("PanelHeader_CollapseTooltip");

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
    /// Whether the collapse button should be shown. Defaults to true.
    /// </summary>
    public bool ShowCollapseButton
    {
        get => (bool)GetValue(ShowCollapseButtonProperty);
        set => SetValue(ShowCollapseButtonProperty, value);
    }

    public static readonly DependencyProperty ShowCollapseButtonProperty =
        DependencyProperty.Register(
            nameof(ShowCollapseButton),
            typeof(bool),
            typeof(PanelHeader),
            new PropertyMetadata(true, OnShowCollapseButtonChanged));

    private static void OnShowCollapseButtonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PanelHeader header)
        {
            header.CollapseButton.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Whether the panel-focus indicator should be shown. Defaults to true. Set false for panels that show
    /// their focus state elsewhere: the utility panels indicate focus via the Utility Panel rail instead.
    /// </summary>
    public bool ShowFocusIndicator
    {
        get => (bool)GetValue(ShowFocusIndicatorProperty);
        set => SetValue(ShowFocusIndicatorProperty, value);
    }

    public static readonly DependencyProperty ShowFocusIndicatorProperty =
        DependencyProperty.Register(
            nameof(ShowFocusIndicator),
            typeof(bool),
            typeof(PanelHeader),
            new PropertyMetadata(true, OnShowFocusIndicatorChanged));

    private static void OnShowFocusIndicatorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // This callback can fire during XAML parse, before the templated FocusIndicatorControl field is
        // connected, so guard against it being null. Loaded applies the value reliably regardless.
        if (d is PanelHeader header
            && header.FocusIndicatorControl is not null)
        {
            header.FocusIndicatorControl.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public PanelHeader()
    {
        _commandService = ServiceLocator.AcquireService<ICommandService>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        this.InitializeComponent();

        Loaded += PanelHeader_Loaded;
    }

    private void PanelHeader_Loaded(object sender, RoutedEventArgs e)
    {
        // Apply the focus-indicator visibility here as the reliable point: the change callback can fire during
        // XAML parse before the named element is connected.
        FocusIndicatorControl.Visibility = ShowFocusIndicator ? Visibility.Visible : Visibility.Collapsed;

        // The panel identity is declared once on the panel root via FocusTracking.Panel; derive the focus
        // indicator's panel from the nearest such ancestor rather than duplicating the value on the header.
        // The walk runs on Loaded because the header's ancestors are only reachable once the tree is live.
        FocusIndicatorControl.Panel = FocusTracking.FindPanel(this);

        // Loaded also covers a panel that is re-docked into another area, which changes the direction it
        // collapses in.
        var hostArea = WorkspaceLayout.FindArea(this);
        if (hostArea is not null)
        {
            CollapseButtonIcon.Symbol = hostArea.Value.GetCollapseSymbol();
        }
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        // The area is derived from the container the header currently sits in.
        var hostArea = WorkspaceLayout.FindArea(this);
        if (hostArea is null)
        {
            return;
        }

        _commandService.Execute<ISetAreaVisibilityCommand>(command =>
        {
            command.Area = hostArea.Value;
            command.IsVisible = false;
        });
    }

    private void TitleBar_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var hostArea = WorkspaceLayout.FindArea(this);
        if (hostArea is null)
        {
            return;
        }

        // Double-clicking the title bar resets the panel to its default size
        _commandService.Execute<IResetAreaSizeCommand>(command =>
        {
            command.Area = hostArea.Value;
        });
    }
}
