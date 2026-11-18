using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.UserInterface.Platform;

namespace Celbridge.UserInterface.Services.Dialogs;

/// <summary>
/// Shows the progress dialog while any progress dialog token is held, titled with the newest token's title. Logs how
/// long each token was held, and warns about a token held for a long time.
/// </summary>
internal sealed class ProgressDialogController
{
    private readonly ILogger _logger;
    private readonly IDialogFactory _dialogFactory;
    private readonly IMessengerService _messengerService;
    private readonly Lock _tokenLock = new();
    private readonly List<ProgressDialogToken> _tokens = [];
    private IProgressDialog? _dialog;
    private IDisposable? _occlusionScope;
    private bool _isSuppressed;

    // How long a token is held before a warning is logged, and how long between repeated warnings.
    internal TimeSpan WarningInterval { get; set; } = TimeSpan.FromMinutes(2);

    public ProgressDialogController(ILogger logger, IDialogFactory dialogFactory, IMessengerService messengerService)
    {
        _logger = logger;
        _dialogFactory = dialogFactory;
        _messengerService = messengerService;
    }

    /// <summary>
    /// Returns true while any token is held.
    /// </summary>
    public bool IsActive
    {
        get
        {
            lock (_tokenLock)
            {
                return _tokens.Count > 0;
            }
        }
    }

    /// <summary>
    /// Acquires a token with the title, and shows the dialog unless it is suppressed. Dispose the token to release it.
    /// </summary>
    public IProgressDialogToken Acquire(string titleText)
    {
        var token = new ProgressDialogToken(titleText, Release);

        lock (_tokenLock)
        {
            _tokens.Add(token);
        }

        _logger.LogInformation("Progress '{Title}' started", titleText);
        _ = WatchTokenAsync(token);

        Update();
        return token;
    }

    /// <summary>
    /// Hides the dialog while suppressed is true. Once the suppression ends, a held token shows the dialog again.
    /// </summary>
    public void SetSuppressed(bool suppressed)
    {
        _isSuppressed = suppressed;
        Update();
    }

    private void Release(ProgressDialogToken token)
    {
        // The message goes out while the token still counts, so a listener has the release time before IsActive turns
        // false.
        _messengerService.Send(new ProgressDialogReleasedMessage());

        lock (_tokenLock)
        {
            _tokens.Remove(token);
        }

        _logger.LogInformation("Progress '{Title}' ended after {DurationMs}ms",
            token.DialogTitle,
            (long)token.HeldDuration.TotalMilliseconds);

        Update();
    }

    // Logs a warning each time the token has been held for another WarningInterval. A progress dialog that stays up
    // this long can mean the operation behind it has stopped.
    private async Task WatchTokenAsync(ProgressDialogToken token)
    {
        var released = token.Released;

        try
        {
            while (true)
            {
                await Task.Delay(WarningInterval, released).ConfigureAwait(false);

                _logger.LogWarning("Progress '{Title}' has run for {Seconds}s without ending",
                    token.DialogTitle,
                    (long)token.HeldDuration.TotalSeconds);
            }
        }
        catch (OperationCanceledException)
        {
            // The token was released.
        }
    }

    private void Update()
    {
        bool hasTokens;
        string? newestTitle = null;

        lock (_tokenLock)
        {
            hasTokens = _tokens.Count > 0;
            if (hasTokens)
            {
                newestTitle = _tokens[^1].DialogTitle;
            }
        }

        bool showDialog = hasTokens && !_isSuppressed;

        if (showDialog)
        {
            if (_dialog is null)
            {
                _dialog = _dialogFactory.CreateProgressDialog();
                _dialog.ShowDialog();
                _occlusionScope = MacOSModalOcclusionMonitor.BeginDialogScope("ProgressDialog");
            }

            _dialog.TitleText = newestTitle!;
        }
        else
        {
            if (_dialog is not null)
            {
                _dialog.HideDialog();
                _dialog = null;
                _occlusionScope?.Dispose();
                _occlusionScope = null;
            }
        }
    }
}
