using Celbridge.Documents.ViewModels;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Documents.Views;

/// <summary>
/// Handles IHostDocument RPC methods for contribution document views.
/// Manages document initialization, loading, saving, change tracking, report writing and the editor's status pip.
/// </summary>
internal sealed class CustomDocumentHandler : IHostDocument
{
    private readonly CustomDocumentViewModel _viewModel;
    private readonly ILogger _logger;
    private readonly IProjectService _projectService;
    private readonly IReportWriter _reportWriter;
    private readonly Func<DocumentMetadata> _createMetadata;
    private readonly Func<bool> _completeSave;
    private readonly Action<WorkspaceItemPip?> _setPip;

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
        Func<bool> completeSave,
        Action<WorkspaceItemPip?> setPip)
    {
        _viewModel = viewModel;
        _logger = logger;
        _projectService = projectService;
        _reportWriter = reportWriter;
        _createMetadata = createMetadata;
        _completeSave = completeSave;
        _setPip = setPip;
    }

    public async Task<InitializeResult> InitializeAsync(string protocolVersion)
    {
        DocumentRpcMethods.ValidateProtocolVersion(protocolVersion);

        // A page that starts again, for any reason, starts with no pip.
        _setPip(null);

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

    public async Task<SetPipResult> SetPipAsync(string kind, string label, bool pulse = false)
    {
        await Task.CompletedTask;

        // An unknown kind is a mistake in the editor. A guessed kind could show an alert in a calmer color and
        // mislead the user.
        if (!TryParsePipKind(kind, out var pipKind))
        {
            throw new ArgumentException(
                $"Unknown pip kind: '{kind}'. Expected 'neutral', 'success', 'notice' or 'alert'.",
                nameof(kind));
        }

        var firstLine = ReadFirstLine(label);
        if (firstLine.Length == 0)
        {
            throw new ArgumentException("Pip label is empty.", nameof(label));
        }

        var pip = new WorkspaceItemPip(pipKind, firstLine, pulse);
        _setPip(pip);

        return new SetPipResult();
    }

    public async Task<ClearPipResult> ClearPipAsync()
    {
        await Task.CompletedTask;

        _setPip(null);

        return new ClearPipResult();
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

    private static bool TryParsePipKind(string kind, out PipKind parsedKind)
    {
        switch (kind?.ToLowerInvariant())
        {
            case "neutral":
                parsedKind = PipKind.Neutral;
                return true;

            case "success":
                parsedKind = PipKind.Success;
                return true;

            case "notice":
                parsedKind = PipKind.Notice;
                return true;

            case "alert":
                parsedKind = PipKind.Alert;
                return true;

            default:
                parsedKind = PipKind.Neutral;
                return false;
        }
    }

    // Returns the first line of the label with the surrounding whitespace removed, or an empty string for a
    // label with no text.
    private static string ReadFirstLine(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        var lines = label.Trim().Split('\n');
        var firstLine = lines[0].Trim();

        return firstLine;
    }
}
