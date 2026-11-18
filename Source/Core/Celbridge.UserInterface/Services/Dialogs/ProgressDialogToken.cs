using System.Diagnostics;
using Celbridge.Dialog;

namespace Celbridge.UserInterface.Services.Dialogs;

public sealed class ProgressDialogToken : IProgressDialogToken
{
    private readonly Action<ProgressDialogToken>? _releaseAction;
    private readonly Stopwatch _heldTimer = Stopwatch.StartNew();
    private readonly CancellationTokenSource _releasedSource = new();
    private bool _disposed;

    public string DialogTitle { get; }

    /// <summary>
    /// Returns how long the token has been held. After the release, returns how long the token was held.
    /// </summary>
    public TimeSpan HeldDuration => _heldTimer.Elapsed;

    /// <summary>
    /// Returns a cancellation token that is cancelled when the token is released.
    /// </summary>
    public CancellationToken Released => _releasedSource.Token;

    public ProgressDialogToken(string dialogTitle, Action<ProgressDialogToken>? releaseAction = null)
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
        _heldTimer.Stop();
        _releaseAction?.Invoke(this);
        _releasedSource.Cancel();
    }
}
