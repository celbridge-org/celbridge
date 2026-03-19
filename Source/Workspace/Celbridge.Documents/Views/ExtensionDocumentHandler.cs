using Celbridge.Documents.ViewModels;
using Celbridge.Host;
using Celbridge.Logging;

namespace Celbridge.Documents.Views;

/// <summary>
/// Handles IHostDocument RPC methods for extension document views.
/// Manages document initialization, loading, saving, and change tracking.
/// </summary>
internal sealed class ExtensionDocumentHandler : IHostDocument
{
    private readonly ExtensionDocumentViewModel _viewModel;
    private readonly ILogger _logger;
    private readonly Func<DocumentMetadata> _createMetadata;
    private readonly Func<bool> _completeSave;

    /// <summary>
    /// Set by the owning view before requesting a save from JS.
    /// The handler completes this when SaveAsync is called back by the extension.
    /// </summary>
    internal TaskCompletionSource<Result>? SaveResultTcs { get; set; }

    public ExtensionDocumentHandler(
        ExtensionDocumentViewModel viewModel,
        ILogger logger,
        Func<DocumentMetadata> createMetadata,
        Func<bool> completeSave)
    {
        _viewModel = viewModel;
        _logger = logger;
        _createMetadata = createMetadata;
        _completeSave = completeSave;
    }

    public async Task<InitializeResult> InitializeAsync(string protocolVersion)
    {
        DocumentRpcMethods.ValidateProtocolVersion(protocolVersion);

        var content = await _viewModel.LoadTextContentAsync();
        var metadata = _createMetadata();

        return new InitializeResult(content, metadata);
    }

    public async Task<LoadResult> LoadAsync()
    {
        var content = await _viewModel.LoadTextContentAsync();
        var metadata = _createMetadata();

        return new LoadResult(content, metadata);
    }

    public async Task<SaveResult> SaveAsync(string content)
    {
        try
        {
            var saveResult = await _viewModel.SaveTextContentAsync(content);

            if (saveResult.IsFailure)
            {
                _logger.LogError(saveResult, "Failed to save extension document");
                _completeSave();
                SaveResultTcs?.TrySetResult(saveResult);
                return new SaveResult(false, saveResult.Error);
            }

            _viewModel.OnSaveCompleted();

            if (_completeSave())
            {
                _logger.LogDebug("Processing pending save request");
                _viewModel.OnDataChanged();
            }

            SaveResultTcs?.TrySetResult(Result.Ok());
            return new SaveResult(true, null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Exception during extension save");
            _completeSave();
            var failResult = Result.Fail("Exception during save").WithException(exception);
            SaveResultTcs?.TrySetResult(failResult);
            return new SaveResult(false, exception.Message);
        }
    }

    public void OnDocumentChanged()
    {
        _viewModel.OnDataChanged();
    }
}
