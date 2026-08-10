using Celbridge.WorkspaceUI.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.WorkspaceUI.Views;

/// <summary>
/// The strip of project-notification banners docked below the workspace document area. It sizes to
/// its content rather than occupying layout space, so it takes no space while no banner is showing.
/// </summary>
public sealed partial class NotificationBar : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    private string ReloadProjectText => _stringLocalizer.GetString("NotificationBar_ReloadProjectButton");

    public NotificationBarViewModel ViewModel { get; }

    public NotificationBar()
    {
        // Acquire the localizer and view model before InitializeComponent so the x:Bind bindings
        // (ReloadProjectText, ViewModel.*) have their sources ready when they first evaluate.
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<NotificationBarViewModel>();

        this.InitializeComponent();
    }

    public void Cleanup()
    {
        ViewModel.Cleanup();
    }
}
