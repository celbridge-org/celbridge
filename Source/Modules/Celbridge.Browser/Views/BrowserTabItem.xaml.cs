using Celbridge.Browser.ViewModels;
using Celbridge.UserInterface.Views.Controls;
using Microsoft.Extensions.Localization;

namespace Celbridge.Browser.Views;

/// <summary>
/// One browser tab in the tab bar: the page's favicon, the tab's label, and the close button from the TabViewItem
/// template. The tab has the shape of a document tab. The page shows outside the tab bar.
/// </summary>
public sealed partial class BrowserTabItem : TabViewItem
{
    private const string CloseButtonAutomationId = "BrowserTabCloseButton";

    public BrowserTabViewModel Tab { get; }

    public BrowserTabItem(BrowserTabViewModel tab)
    {
        // The tab backs x:Bind paths, so it must be set before InitializeComponent evaluates them.
        Tab = tab;

        InitializeComponent();

        // The tab takes the strip band less the gap it keeps above itself.
        Height = TabStripView.TabHeight;
        Margin = new Thickness(0, TabStripView.TabTopGap, 0, 0);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild("CloseButton") is not Button closeButton)
        {
            return;
        }

        var stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        string closeTooltip = stringLocalizer.GetString("Browser_TabBar_CloseTabTooltip");

        ToolTipService.SetToolTip(closeButton, closeTooltip);
        AutomationProperties.SetName(closeButton, closeTooltip);
        AutomationProperties.SetAutomationId(closeButton, CloseButtonAutomationId);
    }
}
