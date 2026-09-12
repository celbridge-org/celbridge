using Celbridge.UserInterface;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Views;

public sealed partial class ExplorerToolbar : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    // The named children the state applies to exist only once InitializeComponent has run, and the
    // property can be set from markup before then.
    private bool _initialized;

    public static readonly DependencyProperty ShowHiddenFilesProperty = DependencyProperty.Register(
        nameof(ShowHiddenFiles),
        typeof(bool),
        typeof(ExplorerToolbar),
        new PropertyMetadata(false, OnShowHiddenFilesChanged));

    // Toolbar tooltip strings
    private string NewFileTooltipString => _stringLocalizer.GetString("ResourceTreeToolbar_NewFileTooltip");
    private string NewFolderTooltipString => _stringLocalizer.GetString("ResourceTreeToolbar_NewFolderTooltip");
    private string CollapseFoldersTooltipString => _stringLocalizer.GetString("ResourceTreeToolbar_CollapseFoldersTooltip");

    public event EventHandler? NewFileClicked;

    public event EventHandler? NewFolderClicked;

    public event EventHandler? CollapseFoldersClicked;

    public event EventHandler? ShowHiddenFilesClicked;

    /// <summary>
    /// Whether the tree is currently drawing hidden resources. Drives the button's glyph and tooltip.
    /// </summary>
    public bool ShowHiddenFiles
    {
        get => (bool)GetValue(ShowHiddenFilesProperty);
        set => SetValue(ShowHiddenFilesProperty, value);
    }

    public ExplorerToolbar()
    {
        // Acquired before InitializeComponent because the tooltip bindings resolve during it.
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        InitializeComponent();

        _initialized = true;

        ApplyShowHiddenFilesState();
    }

    private static void OnShowHiddenFilesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ExplorerToolbar toolbar)
        {
            toolbar.ApplyShowHiddenFilesState();
        }
    }

    // The glyph names what the tree is doing now, and the tooltip names what clicking does next.
    private void ApplyShowHiddenFilesState()
    {
        if (!_initialized)
        {
            return;
        }

        ShowHiddenFilesIcon.Symbol = ShowHiddenFiles ? IconSymbol.Visible : IconSymbol.Hidden;

        var tooltipKey = ShowHiddenFiles
            ? "ResourceTreeToolbar_HideHiddenFilesTooltip"
            : "ResourceTreeToolbar_ShowHiddenFilesTooltip";
        var tooltip = _stringLocalizer.GetString(tooltipKey).Value;

        ToolTipService.SetToolTip(ShowHiddenFilesButton, tooltip);
        AutomationProperties.SetName(ShowHiddenFilesButton, tooltip);
    }

    private void NewFileButton_Click(object sender, RoutedEventArgs e)
    {
        NewFileClicked?.Invoke(this, EventArgs.Empty);
    }

    private void NewFolderButton_Click(object sender, RoutedEventArgs e)
    {
        NewFolderClicked?.Invoke(this, EventArgs.Empty);
    }

    private void CollapseFoldersButton_Click(object sender, RoutedEventArgs e)
    {
        CollapseFoldersClicked?.Invoke(this, EventArgs.Empty);
    }

    private void ShowHiddenFilesButton_Click(object sender, RoutedEventArgs e)
    {
        ShowHiddenFilesClicked?.Invoke(this, EventArgs.Empty);
    }
}
