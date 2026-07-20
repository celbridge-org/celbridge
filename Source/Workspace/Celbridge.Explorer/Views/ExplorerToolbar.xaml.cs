using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Views;

public sealed partial class ExplorerToolbar : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    // Toolbar tooltip strings
    private string NewFileTooltipString => _stringLocalizer.GetString("ResourceTreeToolbar_NewFileTooltip");
    private string NewFolderTooltipString => _stringLocalizer.GetString("ResourceTreeToolbar_NewFolderTooltip");
    private string CollapseFoldersTooltipString => _stringLocalizer.GetString("ResourceTreeToolbar_CollapseFoldersTooltip");

    public event EventHandler? NewFileClicked;

    public event EventHandler? NewFolderClicked;

    public event EventHandler? CollapseFoldersClicked;

    public ExplorerToolbar()
    {
        InitializeComponent();

        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
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
}
