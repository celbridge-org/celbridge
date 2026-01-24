using Celbridge.Dialog;

namespace Celbridge.UserInterface.Services.Dialogs;

public sealed class ProgressDialogToken : IProgressDialogToken
{
    private readonly Action<IProgressDialogToken>? _releaseAction;
    private bool _disposed;

    public string DialogTitle { get; }

    public ProgressDialogToken(string dialogTitle, Action<IProgressDialogToken>? releaseAction = null)
    {
        DialogTitle = dialogTitle;
        _releaseAction = releaseAction;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _releaseAction?.Invoke(this);
    }
}
