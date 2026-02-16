using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// A reusable panel header control.
/// </summary>
public sealed partial class PanelHeader : UserControl
{
    private readonly ICommandService _commandService;

    /// <summary>
    /// The panel this header is associated with.
    /// </summary>
    public FocusablePanel Panel
    {
        get => (FocusablePanel)GetValue(PanelProperty);
        set => SetValue(PanelProperty, value);
    }

    public static readonly DependencyProperty PanelProperty =
        DependencyProperty.Register(
            nameof(Panel),
            typeof(FocusablePanel),
            typeof(PanelHeader),
            new PropertyMetadata(FocusablePanel.None, OnPanelChanged));

    private static void OnPanelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PanelHeader header)
        {
            header.FocusIndicatorControl.Panel = (FocusablePanel)e.NewValue;
        }
    }

    /// <summary>
    /// The visibility flag used to collapse this panel when the close button is clicked.
    /// </summary>
    public PanelVisibilityFlags VisibilityFlag
    {
        get => (PanelVisibilityFlags)GetValue(VisibilityFlagProperty);
        set => SetValue(VisibilityFlagProperty, value);
    }

    public static readonly DependencyProperty VisibilityFlagProperty =
        DependencyProperty.Register(
            nameof(VisibilityFlag),
            typeof(PanelVisibilityFlags),
            typeof(PanelHeader),
            new PropertyMetadata(PanelVisibilityFlags.None));

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
        if (VisibilityFlag == PanelVisibilityFlags.None)
        {
            return;
        }

        _commandService.Execute<ISetPanelVisibilityCommand>(command =>
        {
            command.Panels = VisibilityFlag;
            command.IsVisible = false;
        });
    }

    private void TitleBar_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (VisibilityFlag == PanelVisibilityFlags.None)
        {
            return;
        }

        // Double-clicking the title bar resets the panel to its default size
        _commandService.Execute<IResetPanelSizeCommand>(command =>
        {
            command.Panel = VisibilityFlag;
        });
    }
}
