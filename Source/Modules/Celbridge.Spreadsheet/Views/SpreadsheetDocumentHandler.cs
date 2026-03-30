using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Spreadsheet.ViewModels;

namespace Celbridge.Spreadsheet.Views;

/// <summary>
/// Handles IHostDocument RPC methods for spreadsheet document views.
/// Manages document initialization, loading, saving, import tracking, and change notifications.
/// </summary>
internal sealed class SpreadsheetDocumentHandler : IHostDocument
{
    private readonly SpreadsheetDocumentViewModel _viewModel;
    private readonly ILogger _logger;
    private readonly Func<DocumentMetadata> _createMetadata;
    private readonly Func<Task<string>> _loadSpreadsheetAsBase64;
    private readonly Func<bool> _completeSave;
    private readonly Action _notifyExternalChange;

    /// <summary>
    /// Set by the owning view before requesting a save from JS.
    /// The handler completes this when SaveAsync is called back by the spreadsheet editor.
    /// </summary>
    internal TaskCompletionSource<Result>? SaveResultTcs { get; set; }

    /// <summary>
    /// Whether a spreadsheet import is currently in progress.
    /// </summary>
    internal bool IsImportInProgress { get; set; }

    /// <summary>
    /// Whether another import was requested while one was already in progress.
    /// </summary>
    internal bool HasPendingImport { get; set; }

    public SpreadsheetDocumentHandler(
        SpreadsheetDocumentViewModel viewModel,
        ILogger logger,
        Func<DocumentMetadata> createMetadata,
        Func<Task<string>> loadSpreadsheetAsBase64,
        Func<bool> completeSave,
        Action notifyExternalChange)
    {
        _viewModel = viewModel;
        _logger = logger;
        _createMetadata = createMetadata;
        _loadSpreadsheetAsBase64 = loadSpreadsheetAsBase64;
        _completeSave = completeSave;
        _notifyExternalChange = notifyExternalChange;
    }

    public async Task<InitializeResult> InitializeAsync(string protocolVersion)
    {
        DocumentRpcMethods.ValidateProtocolVersion(protocolVersion);

        var base64Content = await _loadSpreadsheetAsBase64();
        var metadata = _createMetadata();

        IsImportInProgress = true;

        return new InitializeResult(base64Content, metadata);
    }

    public async Task<LoadResult> LoadAsync()
    {
        IsImportInProgress = true;

        var base64Content = await _loadSpreadsheetAsBase64();
        var metadata = _createMetadata();

        return new LoadResult(base64Content, metadata);
    }

    public async Task<SaveResult> SaveAsync(string content)
    {
        try
        {
            var saveResult = await _viewModel.SaveSpreadsheetDataToFile(content);

            if (saveResult.IsFailure)
            {
                _logger.LogError(saveResult, "Failed to save spreadsheet data");
                return CompleteSaveWithResult(saveResult);
            }

            await _viewModel.SaveDocument();

            if (_completeSave())
            {
                _logger.LogDebug("Processing pending save request");
                _viewModel.OnDataChanged();
            }

            return SignalSaveResult(Result.Ok(), success: true);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Exception during save");
            var failResult = Result.Fail("Exception during save").WithException(exception);
            return CompleteSaveWithResult(failResult, exception.Message);
        }
    }

    public void OnDocumentChanged()
    {
        _viewModel.OnDataChanged();
    }

    public void OnImportComplete(bool success, string? error = null)
    {
        IsImportInProgress = false;

        if (!success)
        {
            _logger.LogWarning($"Spreadsheet import failed: {error}");
        }
        else
        {
            _logger.LogDebug("Spreadsheet import completed successfully");
        }

        if (HasPendingImport)
        {
            _logger.LogDebug("Processing pending import request");
            HasPendingImport = false;
            IsImportInProgress = true;
            _notifyExternalChange();
        }
    }

    private SaveResult CompleteSaveWithResult(Result failResult, string? errorMessage = null)
    {
        _completeSave();
        SaveResultTcs?.TrySetResult(failResult);
        return new SaveResult(false, errorMessage ?? failResult.Error);
    }

    private SaveResult SignalSaveResult(Result result, bool success, string? errorMessage = null)
    {
        SaveResultTcs?.TrySetResult(result);
        return new SaveResult(success, errorMessage);
    }
}
