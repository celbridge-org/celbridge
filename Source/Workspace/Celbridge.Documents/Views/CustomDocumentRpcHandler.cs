using Celbridge.Documents.ViewModels;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Patterns;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.Resources;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Documents.Views;

/// <summary>
/// Callbacks into the view that owns a contribution document's RPC handler. SendFilesChanged sends a watch's batch of
/// changes to the page.
/// </summary>
internal sealed record CustomDocumentCallbacks(
    Func<DocumentMetadata> CreateMetadata,
    Func<bool> CompleteSave,
    Action<WorkspaceItemPip?> SetPip,
    Func<int, IReadOnlyList<WatchedFileChange>, Task> SendFilesChanged);

/// <summary>
/// Handles IHostDocument RPC methods for contribution document views.
/// Manages document initialization, loading, saving, change tracking, report writing, the editor's status pip and
/// the page's watches on files.
/// </summary>
internal sealed class CustomDocumentRpcHandler : IHostDocument
{
    private readonly CustomDocumentViewModel _viewModel;
    private readonly ILogger _logger;
    private readonly IProjectService _projectService;
    private readonly IReportWriter _reportWriter;
    private readonly IResourceMonitor _resourceMonitor;
    private readonly CustomDocumentCallbacks _callbacks;

    // The page's watches, by watch id.
    private readonly Dictionary<int, IMatcherSubscription> _watches = new();

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

    public CustomDocumentRpcHandler(
        CustomDocumentViewModel viewModel,
        ILogger logger,
        IProjectService projectService,
        IReportWriter reportWriter,
        IResourceMonitor resourceMonitor,
        CustomDocumentCallbacks callbacks)
    {
        _viewModel = viewModel;
        _logger = logger;
        _projectService = projectService;
        _reportWriter = reportWriter;
        _resourceMonitor = resourceMonitor;
        _callbacks = callbacks;
    }

    public async Task<InitializeResult> InitializeAsync(string protocolVersion)
    {
        DocumentRpcMethods.ValidateProtocolVersion(protocolVersion);

        // A page that starts again, for any reason, starts with the pip cleared and every watch ended.
        _callbacks.SetPip(null);
        EndAllWatches();

        var content = await _viewModel.LoadTextContentAsync();
        var metadata = _callbacks.CreateMetadata();

        return new InitializeResult(content, metadata);
    }

    public async Task<LoadResult> LoadAsync()
    {
        var content = await _viewModel.LoadTextContentAsync();
        var metadata = _callbacks.CreateMetadata();

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
                _callbacks.CompleteSave();
                SaveResultTcs?.TrySetResult(saveResult);
                return new SaveResult(false, saveResult.DiagnosticReport);
            }

            _viewModel.OnSaveCompleted();

            if (_callbacks.CompleteSave())
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
            _callbacks.CompleteSave();
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
        _callbacks.SetPip(pip);

        return new SetPipResult();
    }

    public async Task<ClearPipResult> ClearPipAsync()
    {
        await Task.CompletedTask;

        _callbacks.SetPip(null);

        return new ClearPipResult();
    }

    public async Task<WatchFilesResult> WatchFilesAsync(int watchId, string root = "project:", string include = "", string exclude = "")
    {
        await Task.CompletedTask;

        if (_watches.ContainsKey(watchId))
        {
            throw new ArgumentException($"Watch id {watchId} is already in use.", nameof(watchId));
        }

        var rootName = ReadRootName(root);

        var filterResult = SearchFilter.Compile(include ?? string.Empty, exclude ?? string.Empty);
        if (filterResult.IsFailure)
        {
            throw new ArgumentException(filterResult.FirstErrorMessage);
        }
        var filter = filterResult.Value;

        var handlers = new ResourceChangeHandlers(
            batch => SendChanges(watchId, batch),
            resync => SendResync(watchId, resync));

        var subscribeResult = _resourceMonitor.SubscribeToMatcher(rootName, filter, handlers);
        if (subscribeResult.IsFailure)
        {
            throw new ArgumentException(subscribeResult.FirstErrorMessage);
        }

        _watches[watchId] = subscribeResult.Value;

        return new WatchFilesResult();
    }

    public async Task<UnwatchFilesResult> UnwatchFilesAsync(int watchId)
    {
        await Task.CompletedTask;

        if (_watches.Remove(watchId, out var subscription))
        {
            subscription.Dispose();
        }

        return new UnwatchFilesResult();
    }

    /// <summary>
    /// Ends every watch the page started.
    /// </summary>
    internal void EndAllWatches()
    {
        foreach (var subscription in _watches.Values)
        {
            subscription.Dispose();
        }

        _watches.Clear();
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

    // Returns the root name of a root key, like "project" for "project:".
    private static string ReadRootName(string? root)
    {
        if (string.IsNullOrEmpty(root)
            || !root.EndsWith(':')
            || !ResourceKey.TryCreate(root, out var rootKey)
            || !rootKey.IsEmpty)
        {
            throw new ArgumentException(
                $"Invalid root: '{root}'. Expected a root key like 'project:', 'temp:' or 'utils:'.",
                nameof(root));
        }

        return rootKey.Root;
    }

    private void SendChanges(int watchId, ResourceChangeBatch batch)
    {
        var changes = new List<WatchedFileChange>();
        foreach (var change in batch.Changes)
        {
            var resource = change.Resource.ToString();
            var state = FormatState(change.State);
            changes.Add(new WatchedFileChange(resource, state));
        }

        _ = SendFilesChangedAsync(watchId, changes);
    }

    // Lost events arrive as a change to the root's own folder. A page lists a changed folder again, so the page
    // lists the whole root.
    private void SendResync(int watchId, ResourceResync resync)
    {
        var rootKey = ResourceKey.Create(resync.RootName + ":");
        var changes = new List<WatchedFileChange>
        {
            new WatchedFileChange(rootKey.ToString(), FormatState(ResourceItemState.Folder)),
        };

        _ = SendFilesChangedAsync(watchId, changes);
    }

    private async Task SendFilesChangedAsync(int watchId, IReadOnlyList<WatchedFileChange> changes)
    {
        try
        {
            await _callbacks.SendFilesChanged(watchId, changes);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to send the file changes of watch {WatchId} to the editor", watchId);
        }
    }

    private static string FormatState(ResourceItemState state) => state.ToString().ToLowerInvariant();

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
