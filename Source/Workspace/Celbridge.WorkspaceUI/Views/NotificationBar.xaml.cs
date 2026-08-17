using Celbridge.WorkspaceUI.ViewModels;

namespace Celbridge.WorkspaceUI.Views;

/// <summary>
/// The project-notification banner docked below the workspace document area. It sizes to its content rather
/// than occupying layout space, so it takes no space while no notification is showing.
/// </summary>
public sealed partial class NotificationBar : UserControl
{
    public NotificationBarViewModel ViewModel { get; }

    public NotificationBar()
    {
        // Acquire the view model before InitializeComponent so the x:Bind bindings have their source ready
        // when they first evaluate.
        ViewModel = ServiceLocator.AcquireService<NotificationBarViewModel>();

        this.InitializeComponent();
    }

    // The banner is reused across notifications, so it closes both when the user dismisses it and whenever
    // the view model takes it down to show the next one. Only the former advances.
    private void OnBannerClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (args.Reason != InfoBarCloseReason.CloseButton)
        {
            return;
        }

        ViewModel.OnBannerDismissed();
    }

    public void Cleanup()
    {
        ViewModel.Cleanup();
    }
}
