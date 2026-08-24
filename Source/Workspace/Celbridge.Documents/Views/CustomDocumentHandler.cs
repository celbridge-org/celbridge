using Celbridge.Documents.ViewModels;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.Utilities;

namespace Celbridge.Documents.Views;

/// <summary>
/// Handles IHostDocument RPC methods for contribution document views.
/// Manages document initialization, loading, saving, change tracking, and report writing.
/// </summary>
internal sealed class CustomDocumentHandler : IHostDocument
{
    private readonly CustomDocumentViewModel _viewModel;
    private readonly ILogger _logger;
    private readonly IProjectService _projectService;
    private readonly IReportWriter _reportWriter;
    private readonly Func<DocumentMetadata> _createMetadata;
    private readonly Func<bool> _completeSave;

    /// <summary>
    /// Set by the owning view before requesting a save from JS.
    /// The handler completes this when SaveAsync is called back by the contribution.
    /// </summary>
    internal TaskCompletionSource<Result>? SaveResultTcs { get; set; }

    /// <summary>
    /// Raised when the WebView client signals that content has been loaded and the editor is ready.
    /// The argument distinguishes the initial load from an external-change reload.
    /// </summary>
    internal event Action<ContentLoadedReason>? ContentLoaded;

    public CustomDocumentHandler(
        CustomDocumentViewModel viewModel,
        ILogger logger,
        IProjectService projectService,
        IReportWriter reportWriter,
        Func<DocumentMetadata> createMetadata,
        Func<bool> completeSave)
    {
        _viewModel = viewModel;
        _logger = logger;
        _projectService = projectService;
        _reportWriter = reportWriter;
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
                _logger.LogError(saveResult, "Failed to save contribution document");
                _completeSave();
                SaveResultTcs?.TrySetResult(saveResult);
                return new SaveResult(false, saveResult.DiagnosticReport);
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
            _logger.LogError(exception, "Exception during contribution save");
            _completeSave();
            var failResult = Result.Fail("Exception during save").WithException(exception);
            SaveResultTcs?.TrySetResult(failResult);
            return new SaveResult(false, exception.Message);
        }
    }

    public async Task<WriteReportResult> WriteReportAsync(string reportJson)
    {
        var currentProject = _projectService.CurrentProject;
        if (currentProject is null)
        {
            throw new InvalidOperationException("No project is loaded.");
        }

        var parseResult = ReportSerializer.Deserialize(reportJson);
        if (parseResult.IsFailure)
        {
            throw new ArgumentException(parseResult.MessageChain, nameof(reportJson));
        }

        var report = parseResult.Value;

        // The id names a file and the glob that prunes its history, so it is checked before anything
        // touches the disk rather than left to fail inside the write.
        if (!ReportLocation.IsValidReportId(report.Id))
        {
            throw new ArgumentException(
                $"Invalid report id: '{report.Id}'. Expected lowercase letters, digits, hyphens and dots.",
                nameof(reportJson));
        }

        var writeResult = await ReportLocation.WriteReportAsync(_reportWriter, report, currentProject.ProjectDataFolderPath);
        if (writeResult.IsFailure)
        {
            throw new InvalidOperationException(writeResult.MessageChain);
        }

        var reportResource = writeResult.Value;

        return new WriteReportResult(reportResource.ToString());
    }

    public void OnDocumentChanged()
    {
        _viewModel.OnDataChanged();
    }

    public void OnContentLoaded(ContentLoadedReason reason = ContentLoadedReason.Initial)
    {
        ContentLoaded?.Invoke(reason);
    }

    public void OnImportComplete(bool success, string? error = null)
    {
        if (success)
        {
            return;
        }

        var detail = string.IsNullOrEmpty(error) ? "no detail reported" : error;

        _logger.LogError($"Editor failed to import '{_viewModel.FileResource}': {detail}");
    }
}
