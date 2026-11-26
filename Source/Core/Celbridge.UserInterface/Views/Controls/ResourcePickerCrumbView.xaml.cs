using Celbridge.UserInterface.ViewModels.Controls;
using Microsoft.UI.Xaml.Automation;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// Draws one part of the resource picker view's breadcrumb. The project folder's part shows a folder icon. The
/// project's name is the icon's tooltip and accessible name. Every other part shows the folder's name.
/// </summary>
public sealed partial class ResourcePickerCrumbView : UserControl
{
    public ResourcePickerCrumbView()
    {
        this.InitializeComponent();
    }

    // The part reads the crumb from the data context. The breadcrumb also draws the ellipsis item with this
    // template, and the ellipsis item has no data. A compiled binding would fail on the missing data.
    private void CrumbView_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (args.NewValue is not ResourcePickerCrumb crumb)
        {
            ProjectFolderIcon.Visibility = Visibility.Collapsed;
            NameText.Visibility = Visibility.Collapsed;
            return;
        }

        AutomationProperties.SetName(this, crumb.Name);

        if (crumb.IsProjectFolder)
        {
            ToolTipService.SetToolTip(this, crumb.Name);
            ProjectFolderIcon.Visibility = Visibility.Visible;
            NameText.Visibility = Visibility.Collapsed;
            return;
        }

        ToolTipService.SetToolTip(this, null);
        NameText.Text = crumb.Name;
        NameText.Visibility = Visibility.Visible;
        ProjectFolderIcon.Visibility = Visibility.Collapsed;
    }
}
