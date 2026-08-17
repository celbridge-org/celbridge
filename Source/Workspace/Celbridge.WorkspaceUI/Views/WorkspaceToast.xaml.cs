using Celbridge.WorkspaceUI.ViewModels;

namespace Celbridge.WorkspaceUI.Views;

/// <summary>
/// The workspace's single notification toast, overlaid at the bottom-right of the workspace. Which
/// notifications are shown and which replace which is the view model's.
/// </summary>
public sealed partial class WorkspaceToast : UserControl
{
    public WorkspaceToastViewModel ViewModel { get; }

    public WorkspaceToast()
    {
        // Acquire the view model before InitializeComponent so the x:Bind bindings have their source ready
        // when they first evaluate.
        ViewModel = ServiceLocator.AcquireService<WorkspaceToastViewModel>();

        this.InitializeComponent();
    }

    // The InfoBar is reused across notifications, so it closes both when the user dismisses it and when
    // the view model takes it down. Only the former is the user acting on it.
    private void OnToastClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (args.Reason != InfoBarCloseReason.CloseButton)
        {
            return;
        }

        ViewModel.OnToastDismissed();
    }

    public void Cleanup()
    {
        ViewModel.Cleanup();
    }
}
