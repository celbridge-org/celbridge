using Celbridge.Documents.ViewModels;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.UserInterface;
using Celbridge.Utilities;

namespace Celbridge.Documents.Views;

/// <summary>
/// Handles IHostDocument RPC methods for contribution document views.
/// Manages document initialization, loading, saving, change tracking, report writing and the editor's status.
/// </summary>
internal sealed class CustomDocumentHandler : IHostDocument
{
    private readonly CustomDocumentViewModel _viewModel;
    private readonly ILogger _logger;
    private readonly IProjectService _projectService;
    private readonly IReportWriter _reportWriter;
    private readonly Func<DocumentMetadata> _createMetadata;
    private readonly Func<bool> _completeSave;
    private readonly Action<DocumentStatus?> _setStatus;

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
        Action<DocumentStatus?> setStatus)
    {
        _viewModel = viewModel;
        _logger = logger;
        _projectService = projectService;
        _reportWriter = reportWriter;
        _createMetadata = createMetadata;
        _completeSave = completeSave;
        _setStatus = setStatus;
    }

    public async Task<InitializeResult> InitializeAsync(string protocolVersion)
    {
        DocumentRpcMethods.ValidateProtocolVersion(protocolVersion);

        // A page that starts again, for any reason, starts with no status.
        _setStatus(null);

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

    public async Task<SetStatusResult> SetStatusAsync(string kind, string label)
    {
        await Task.CompletedTask;

        // An unknown kind is a mistake in the editor. A guessed kind could show an urgent state in a calmer color
        // and mislead the user.
        if (!TryParseStatusKind(kind, out var statusKind))
        {
            throw new ArgumentException(
                $"Unknown status kind: '{kind}'. Expected 'info', 'success', 'warning' or 'critical'.",
                nameof(kind));
        }

        var firstLine = ReadFirstLine(label);
        if (firstLine.Length == 0)
        {
            throw new ArgumentException("Status label is empty.", nameof(label));
        }

        var status = new DocumentStatus(statusKind, firstLine);
        _setStatus(status);

        return new SetStatusResult();
    }

    public async Task<ClearStatusResult> ClearStatusAsync()
    {
        await Task.CompletedTask;

        _setStatus(null);

        return new ClearStatusResult();
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

    private static bool TryParseStatusKind(string kind, out StatusKind parsedKind)
    {
        switch (kind?.ToLowerInvariant())
        {
            case "info":
                parsedKind = StatusKind.Info;
                return true;

            case "success":
                parsedKind = StatusKind.Success;
                return true;

            case "warning":
                parsedKind = StatusKind.Warning;
                return true;

            case "critical":
                parsedKind = StatusKind.Critical;
                return true;

            default:
                parsedKind = StatusKind.Info;
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
