using Microsoft.Extensions.Localization;

namespace Celbridge.Documents.Views;

public sealed partial class DocumentToolbar : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    // Toolbar tooltip strings
    private string SplitViewTooltipString => _stringLocalizer.GetString("DocumentToolbar_SplitViewTooltip");

    public DocumentToolbar()
    {
        InitializeComponent();

        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
    }

    private void SplitViewButton_Click(object sender, RoutedEventArgs e)
    {
        // Placeholder - Split view functionality not yet implemented
    }
}
