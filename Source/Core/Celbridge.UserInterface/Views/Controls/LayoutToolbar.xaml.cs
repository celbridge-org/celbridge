using Celbridge.Commands;
using Celbridge.Platform;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Views;

public sealed partial class LayoutToolbar : UserControl
{
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWindowModeService _windowModeService;
    private readonly ILayoutService _layoutService;

    private bool _isUpdatingUI = false;
    private bool _isOnWorkspacePage = false;

    public LayoutToolbar()
    {
        InitializeComponent();

        var platformInfo = ServiceLocator.AcquireService<IPlatformInfo>();
        if (platformInfo.HasNativeFullScreenAffordance)
        {
            // macOS provides fullscreen natively through the title-bar green button, so the app does not
            // offer its own Full Screen toggle. The Default/Focus/Presentation layout modes remain available.
            FullScreenToggle.Visibility = Visibility.Collapsed;
        }

        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();
        _windowModeService = ServiceLocator.AcquireService<IWindowModeService>();
        _layoutService = ServiceLocator.AcquireService<ILayoutService>();

        // The flyout opens over the document area, where a hosted web view would take the click too.
        var overlayInputSuppressor = ServiceLocator.AcquireService<IOverlayInputSuppressor>();
        overlayInputSuppressor.SuppressWhileOpen(PanelLayoutFlyout);

        Loaded += LayoutToolbar_Loaded;
        Unloaded += LayoutToolbar_Unloaded;
    }

    private void LayoutToolbar_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyTooltips();
        ApplyLabels();
        UpdatePanelIcons();
        UpdateLayoutModeRadios();
        UpdateFullScreenToggle();
        UpdateBottomAreaAlignmentButtons();
        UpdateWorkspaceControlsVisibility();

        // Register for layout manager state change messages
        _messengerService.Register<LayoutModeChangedMessage>(this, OnLayoutModeChanged);
        _messengerService.Register<FullScreenChangedMessage>(this, OnFullScreenChanged);
        _messengerService.Register<SurfaceVisibilityChangedMessage>(this, OnSurfaceVisibilityChanged);
        _messengerService.Register<BottomAreaAlignmentChangedMessage>(this, OnBottomAreaAlignmentChanged);
        _messengerService.Register<ActivePageChangedMessage>(this, OnActivePageChanged);
        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);
    }

    private void LayoutToolbar_Unloaded(object sender, RoutedEventArgs e)
    {
        _messengerService.UnregisterAll(this);
        Loaded -= LayoutToolbar_Loaded;
        Unloaded -= LayoutToolbar_Unloaded;
    }

    /// <summary>
    /// The controls in this toolbar that a user can click.
    /// </summary>
    internal IReadOnlyList<FrameworkElement> GetInteractiveElements()
    {
        var elements = new List<FrameworkElement>
        {
            PanelLayoutButton,
            ToggleUtilityPanelButton,
            ToggleBottomAreaButton,
            ToggleSideAreaButton
        };

        return elements;
    }

    private void UpdateWorkspaceControlsVisibility()
    {
        // Panel toggles, layout-mode radios, and the reset button only make sense on the Workspace page.
        // The Full Screen toggle stays available everywhere because it is a window-level concern.
        var visibility = _isOnWorkspacePage ? Visibility.Visible : Visibility.Collapsed;

        PanelToggleButtons.Visibility = visibility;
        ResetLayoutSeparator.Visibility = visibility;
        ResetLayoutButton.Visibility = visibility;

        WindowModeHeader.Visibility = visibility;
        DefaultModeRadio.Visibility = visibility;
        FocusModeRadio.Visibility = visibility;
        PresentationModeRadio.Visibility = visibility;

        BottomAreaAlignmentHeader.Visibility = visibility;
        BottomAreaAlignmentButtons.Visibility = visibility;
    }

    private void ApplyTooltips()
    {
        var layoutTooltip = _stringLocalizer.GetString("LayoutToolbar_CustomizeLayoutTooltip");
        ToolTipService.SetToolTip(PanelLayoutButton, layoutTooltip);
        ToolTipService.SetPlacement(PanelLayoutButton, PlacementMode.Bottom);

        var primaryTooltip = _stringLocalizer.GetString("LayoutToolbar_ToggleUtilityPanelTooltip");
        ToolTipService.SetToolTip(ToggleUtilityPanelButton, primaryTooltip);
        ToolTipService.SetPlacement(ToggleUtilityPanelButton, PlacementMode.Bottom);

        var consoleTooltip = _stringLocalizer.GetString("LayoutToolbar_ToggleBottomAreaTooltip");
        ToolTipService.SetToolTip(ToggleBottomAreaButton, consoleTooltip);
        ToolTipService.SetPlacement(ToggleBottomAreaButton, PlacementMode.Bottom);

        var secondaryTooltip = _stringLocalizer.GetString("LayoutToolbar_ToggleSideAreaTooltip");
        ToolTipService.SetToolTip(ToggleSideAreaButton, secondaryTooltip);
        ToolTipService.SetPlacement(ToggleSideAreaButton, PlacementMode.Bottom);

        var defaultModeTooltip = _stringLocalizer.GetString("LayoutToolbar_DefaultModeTooltip");
        ToolTipService.SetToolTip(DefaultModeRadio, defaultModeTooltip);
        ToolTipService.SetPlacement(DefaultModeRadio, PlacementMode.Bottom);

        var focusModeTooltip = _stringLocalizer.GetString("LayoutToolbar_FocusModeTooltip");
        ToolTipService.SetToolTip(FocusModeRadio, focusModeTooltip);
        ToolTipService.SetPlacement(FocusModeRadio, PlacementMode.Bottom);

        var presentationModeTooltip = _stringLocalizer.GetString("LayoutToolbar_PresentationModeTooltip");
        ToolTipService.SetToolTip(PresentationModeRadio, presentationModeTooltip);
        ToolTipService.SetPlacement(PresentationModeRadio, PlacementMode.Bottom);

        var fullScreenTooltip = _stringLocalizer.GetString("LayoutToolbar_FullScreenModeTooltip");
        ToolTipService.SetToolTip(FullScreenToggle, fullScreenTooltip);
        ToolTipService.SetPlacement(FullScreenToggle, PlacementMode.Bottom);

        var alignLeftTooltip = _stringLocalizer.GetString("LayoutToolbar_AlignBottomPanelLeftTooltip");
        ToolTipService.SetToolTip(AlignBottomAreaLeftButton, alignLeftTooltip);
        ToolTipService.SetPlacement(AlignBottomAreaLeftButton, PlacementMode.Bottom);

        var alignCenterTooltip = _stringLocalizer.GetString("LayoutToolbar_AlignBottomPanelCenterTooltip");
        ToolTipService.SetToolTip(AlignBottomAreaCenterButton, alignCenterTooltip);
        ToolTipService.SetPlacement(AlignBottomAreaCenterButton, PlacementMode.Bottom);

        var alignRightTooltip = _stringLocalizer.GetString("LayoutToolbar_AlignBottomPanelRightTooltip");
        ToolTipService.SetToolTip(AlignBottomAreaRightButton, alignRightTooltip);
        ToolTipService.SetPlacement(AlignBottomAreaRightButton, PlacementMode.Bottom);

        var alignJustifyTooltip = _stringLocalizer.GetString("LayoutToolbar_AlignBottomPanelJustifyTooltip");
        ToolTipService.SetToolTip(AlignBottomAreaJustifyButton, alignJustifyTooltip);
        ToolTipService.SetPlacement(AlignBottomAreaJustifyButton, PlacementMode.Bottom);
    }

    private void ApplyLabels()
    {
        ResetLayoutButtonText.Text = _stringLocalizer.GetString("LayoutToolbar_ResetLayoutButton");

        WindowModeHeader.Text = _stringLocalizer.GetString("LayoutToolbar_LayoutModeLabel");
        DefaultModeLabel.Text = _stringLocalizer.GetString("LayoutToolbar_DefaultLabel");
        FocusModeLabel.Text = _stringLocalizer.GetString("LayoutToolbar_FocusLabel");
        PresentationModeLabel.Text = _stringLocalizer.GetString("LayoutToolbar_PresentationLabel");
        FullScreenLabel.Text = _stringLocalizer.GetString("LayoutToolbar_FullScreen");
        BottomAreaAlignmentHeader.Text = _stringLocalizer.GetString("LayoutToolbar_BottomPanelLabel");
    }

    private void OnActivePageChanged(object recipient, ActivePageChangedMessage message)
    {
        _isOnWorkspacePage = message.ActivePage == ApplicationPage.Workspace;
        UpdateWorkspaceControlsVisibility();
    }

    private void OnWorkspaceLoaded(object recipient, WorkspaceLoadedMessage message)
    {
        UpdateWorkspaceControlsVisibility();
    }

    private void OnLayoutModeChanged(object recipient, LayoutModeChangedMessage message)
    {
        UpdatePanelIcons();
        UpdateLayoutModeRadios();
    }

    private void OnFullScreenChanged(object recipient, FullScreenChangedMessage message)
    {
        UpdateFullScreenToggle();
    }

    private void OnSurfaceVisibilityChanged(object recipient, SurfaceVisibilityChangedMessage message)
    {
        UpdatePanelIcons();
    }

    private void OnBottomAreaAlignmentChanged(object recipient, BottomAreaAlignmentChangedMessage message)
    {
        UpdateBottomAreaAlignmentButtons();
    }

    private void UpdateLayoutModeRadios()
    {
        _isUpdatingUI = true;
        try
        {
            var layoutMode = _windowModeService.LayoutMode;
            DefaultModeRadio.IsChecked = layoutMode == LayoutMode.Default;
            FocusModeRadio.IsChecked = layoutMode == LayoutMode.Focus;
            PresentationModeRadio.IsChecked = layoutMode == LayoutMode.Presentation;
        }
        finally
        {
            _isUpdatingUI = false;
        }
    }

    private void UpdateFullScreenToggle()
    {
        _isUpdatingUI = true;
        try
        {
            FullScreenToggle.IsChecked = _windowModeService.IsFullScreen;
        }
        finally
        {
            _isUpdatingUI = false;
        }
    }

    // The four options are mutually exclusive, so they are driven from the layout service rather than from
    // each other. Re-asserting every button also undoes the toggle a click on the active option would
    // otherwise make.
    private void UpdateBottomAreaAlignmentButtons()
    {
        _isUpdatingUI = true;
        try
        {
            var alignment = _layoutService.BottomAreaAlignment;
            AlignBottomAreaLeftButton.IsChecked = alignment == BottomAreaAlignment.Left;
            AlignBottomAreaCenterButton.IsChecked = alignment == BottomAreaAlignment.Center;
            AlignBottomAreaRightButton.IsChecked = alignment == BottomAreaAlignment.Right;
            AlignBottomAreaJustifyButton.IsChecked = alignment == BottomAreaAlignment.Justify;
        }
        finally
        {
            _isUpdatingUI = false;
        }
    }

    private void UpdatePanelIcons()
    {
        UtilityPanelIcon.IsActivePanel = _layoutService.IsUtilityPanelVisible;
        BottomAreaIcon.IsActivePanel = _layoutService.IsBottomAreaVisible;
        SideAreaIcon.IsActivePanel = _layoutService.IsSideAreaVisible;
    }

    private void ToggleUtilityPanelButton_Click(object sender, RoutedEventArgs e)
    {
        // Use command to toggle panel visibility
        var isVisible = !_layoutService.IsUtilityPanelVisible;
        _commandService.Execute<ISetSurfaceVisibilityCommand>(command =>
        {
            command.Surfaces = WorkspaceSurface.UtilityPanel;
            command.IsVisible = isVisible;
        });
    }

    private void ToggleBottomAreaButton_Click(object sender, RoutedEventArgs e)
    {
        // Toggle the Bottom document area's visibility.
        var isVisible = !_layoutService.IsBottomAreaVisible;
        _commandService.Execute<ISetSurfaceVisibilityCommand>(command =>
        {
            command.Surfaces = WorkspaceSurface.BottomArea;
            command.IsVisible = isVisible;
        });
    }

    private void ToggleSideAreaButton_Click(object sender, RoutedEventArgs e)
    {
        // Use command to toggle panel visibility
        var isVisible = !_layoutService.IsSideAreaVisible;
        _commandService.Execute<ISetSurfaceVisibilityCommand>(command =>
        {
            command.Surfaces = WorkspaceSurface.SideArea;
            command.IsVisible = isVisible;
        });
    }

    private void Button_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void ResetLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        _commandService.Execute<ISetLayoutCommand>(command =>
        {
            command.Transition = LayoutTransition.ResetLayout;
        });
        PanelLayoutFlyout.Hide();
    }

    private void LayoutModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUI)
        {
            return;
        }

        LayoutTransition transition;

        if (ReferenceEquals(sender, DefaultModeRadio))
        {
            transition = LayoutTransition.Default;
        }
        else if (ReferenceEquals(sender, FocusModeRadio))
        {
            transition = LayoutTransition.Focus;
        }
        else if (ReferenceEquals(sender, PresentationModeRadio))
        {
            transition = LayoutTransition.Presentation;
        }
        else
        {
            return;
        }

        _commandService.Execute<ISetLayoutCommand>(command =>
        {
            command.Transition = transition;
        });
        PanelLayoutFlyout.Hide();
    }

    // The flyout is left open so the user can step through the alignments and watch the layout change
    // behind it, unlike the layout modes, which are a single choice.
    private void BottomAreaAlignmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUI)
        {
            return;
        }

        BottomAreaAlignment alignment;

        if (ReferenceEquals(sender, AlignBottomAreaLeftButton))
        {
            alignment = BottomAreaAlignment.Left;
        }
        else if (ReferenceEquals(sender, AlignBottomAreaCenterButton))
        {
            alignment = BottomAreaAlignment.Center;
        }
        else if (ReferenceEquals(sender, AlignBottomAreaRightButton))
        {
            alignment = BottomAreaAlignment.Right;
        }
        else if (ReferenceEquals(sender, AlignBottomAreaJustifyButton))
        {
            alignment = BottomAreaAlignment.Justify;
        }
        else
        {
            return;
        }

        if (alignment == _layoutService.BottomAreaAlignment)
        {
            // Clicking the active option unchecked it and changes nothing, so no message comes back to
            // put it right.
            UpdateBottomAreaAlignmentButtons();
            return;
        }

        _commandService.Execute<ISetBottomAreaAlignmentCommand>(command =>
        {
            command.Alignment = alignment;
        });
    }

    private void FullScreenToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUI)
        {
            return;
        }

        _commandService.Execute<ISetLayoutCommand>(command =>
        {
            command.Transition = LayoutTransition.ToggleFullScreen;
        });
    }
}
