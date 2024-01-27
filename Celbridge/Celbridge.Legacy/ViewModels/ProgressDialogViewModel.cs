using CommunityToolkit.Mvvm.Messaging;
using Windows.Foundation;

namespace Celbridge.Legacy.ViewModels;

public partial class ProgressDialogViewModel : ObservableObject
{
    public ProgressDialogViewModel(IMessenger messengerService)
    {
        messengerService.Register<HideProgressDialogMessage>(this, OnHideProgressDialogueMessage);
    }

    [ObservableProperty]
    private string _title = string.Empty;

    public ContentDialog? ContentDialog { get; set; }

    public bool IsCancelEnabled => OnCancel != null; 
    public Action? OnCancel { get; set; }

    public ICommand CancelCommand => new RelayCommand(Cancel_Executed);

    public IAsyncOperation<ContentDialogResult>? AsyncOperation { get; set; }

    private void Cancel_Executed()
    {
        OnCancel?.Invoke();
    }

    private void OnHideProgressDialogueMessage(object recipient, HideProgressDialogMessage message)
    {
        try
        {
            // This call causes an intermittent DisposedObject exception on Windows.
            // Closing and Opening the project repeatedly is usually enough to trigger it.
            // I've spent ages trying to figure out what's causing it, but it doesn't make sense.
            // At this point I think it might be an internal bug in ContentDialog, so it's possible a
            // future WinUI update will fix it.
            // It appears to work fine on both Windows and SkiaGTK if we swallow the exception, so I'm
            // going with that solution for now.
            ContentDialog?.Hide();
        }
        catch (Exception)
        {}
    }
}
