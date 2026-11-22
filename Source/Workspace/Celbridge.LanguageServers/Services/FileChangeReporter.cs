using System.Threading.Channels;
using Celbridge.LanguageServers.Helpers;
using Celbridge.Logging;
using Celbridge.Resources;

namespace Celbridge.LanguageServers.Services;

/// <summary>
/// Receives file events. A reporter makes the events from the project's changes on disk. Safe to call from any
/// thread.
/// </summary>
internal interface IFileChangeReceiver
{
    /// <summary>
    /// Passes the file events to the server. The list contains at least one event.
    /// </summary>
    void ReportFileChanges(IReadOnlyList<FileEvent> fileEvents);
}

/// <summary>
/// Reports the project's file changes to a language server that does not watch the project's files itself. The
/// reporter follows the project's changes through the resource monitor, and turns each change into the file events
/// of a workspace/didChangeWatchedFiles notification. The reporter keeps the paths of the known files and folders,
/// which tells a new file from a changed one.
/// </summary>
internal sealed class FileChangeReporter : IDisposable
{
    // Matches a server's reported files, in any folder.
    private sealed class ReportedFilesMatcher : IPathMatcher
    {
        private readonly ReportedFiles _reportedFiles;

        public ReportedFilesMatcher(ReportedFiles reportedFiles)
        {
            _reportedFiles = reportedFiles;
        }

        public bool IsMatch(string path, bool isFolder)
        {
            if (isFolder)
            {
                return false;
            }

            var fileName = Path.GetFileName(path).ToLowerInvariant();
            var extension = Path.GetExtension(fileName);

            return _reportedFiles.FileNames.Contains(fileName)
                || _reportedFiles.Extensions.Contains(extension);
        }

        public bool CanMatchInside(string folderPath)
        {
            return true;
        }
    }

    // One entry of the reporter's queue: a batch of changes, or a resync when the batch is null.
    private sealed record QueuedChange(ResourceChangeBatch? Batch);

    // The Language Server Protocol's kinds of file event.
    private const int CreatedType = 1;
    private const int ChangedType = 2;
    private const int DeletedType = 3;

    private const char KeySeparator = '/';

    private readonly IResourceMonitor _monitor;
    private readonly IResourceWalker _walker;
    private readonly string _projectFolderPath;
    private readonly IFileChangeReceiver _receiver;
    private readonly ILogger _logger;
    private readonly ReportedFilesMatcher _matcher;

    // One loop reads the queue, so the server gets the changes in order.
    private readonly Channel<QueuedChange> _changes;
    private IMatcherSubscription? _subscription;

    // The key paths of the known files and the known folders. A listing or a change makes a path known. Only the
    // loop reads and writes the sets. A volume may ignore letter case, so the sets do too.
    private readonly HashSet<string> _knownFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _knownFolders = new(StringComparer.OrdinalIgnoreCase);

    public FileChangeReporter(
        IResourceMonitor monitor,
        IResourceWalker walker,
        ReportedFiles reportedFiles,
        string projectFolderPath,
        IFileChangeReceiver receiver,
        ILogger logger)
    {
        _monitor = monitor;
        _walker = walker;
        _projectFolderPath = projectFolderPath;
        _receiver = receiver;
        _logger = logger;
        _matcher = new ReportedFilesMatcher(reportedFiles);

        var channelOptions = new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        };
        _changes = Channel.CreateUnbounded<QueuedChange>(channelOptions);
    }

    /// <summary>
    /// Starts following the project's changes.
    /// </summary>
    public Result Start()
    {
        var handlers = new ResourceChangeHandlers(
            batch => _changes.Writer.TryWrite(new QueuedChange(batch)),
            resync => _changes.Writer.TryWrite(new QueuedChange(null)));

        var subscribeResult = _monitor.SubscribeToMatcher(ResourceKey.DefaultRoot, _matcher, handlers);
        if (subscribeResult.IsFailure)
        {
            return Result.Fail("Failed to follow the project's file changes")
                .WithErrors(subscribeResult);
        }
        _subscription = subscribeResult.Value;

        // The project's files are listed off the UI thread, which delivers each change.
        _ = Task.Run(ReportLoopAsync);

        return Result.Ok();
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
        _changes.Writer.TryComplete();
    }

    private async Task ReportLoopAsync()
    {
        // The subscription is already in place, so a change made during the listing waits in the queue.
        await ListProjectAsync();

        await foreach (var change in _changes.Reader.ReadAllAsync())
        {
            try
            {
                var fileEvents = await CreateFileEventsAsync(change.Batch);
                if (fileEvents.Count > 0)
                {
                    _receiver.ReportFileChanges(fileEvents);
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to report file changes to a language server");
            }
        }
    }

    // Makes the project's reported files and folders known. A server finds the same files when the server starts.
    private async Task ListProjectAsync()
    {
        _knownFiles.Clear();
        _knownFolders.Clear();

        try
        {
            await AddFolderFilesAsync(ResourceKey.Empty, fileEvents: null);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to list the project's files for a language server");
        }
    }

    private async Task<List<FileEvent>> CreateFileEventsAsync(ResourceChangeBatch? batch)
    {
        var fileEvents = new List<FileEvent>();

        if (batch is null)
        {
            // The monitor lost events, so any file may have changed. A changed project folder makes the server
            // check every file again.
            await ListProjectAsync();
            fileEvents.Add(new FileEvent(LspUri.FromPath(_projectFolderPath), ChangedType));
            return fileEvents;
        }

        foreach (var change in batch.Changes)
        {
            var keyPath = change.Resource.Path;
            var uri = LspUri.FromPath(GetPath(keyPath));

            switch (change.State)
            {
                case ResourceItemState.Absent:
                    // The path may have named a file or a folder. A server takes a deleted event for either.
                    ForgetPath(keyPath);
                    fileEvents.Add(new FileEvent(uri, DeletedType));
                    break;

                case ResourceItemState.File:
                    // A change does not say whether the file is new. A created event makes a server check the whole
                    // project again, and a changed event for a new file is ignored. So only a new file is reported
                    // as created.
                    var isNewFile = _knownFiles.Add(keyPath);
                    fileEvents.Add(new FileEvent(uri, isNewFile ? CreatedType : ChangedType));
                    break;

                case ResourceItemState.Folder:
                    // A known folder reports a change whenever an item inside the folder is added or removed. The
                    // item's change arrives separately. A new folder may contain files, and a server ignores an
                    // event for the folder.
                    var isKnownFolder = string.IsNullOrEmpty(keyPath)
                        || _knownFolders.Contains(keyPath);
                    if (!isKnownFolder)
                    {
                        await AddFolderFilesAsync(change.Resource, fileEvents);
                    }
                    break;
            }
        }

        return fileEvents;
    }

    // Lists a folder. Makes the folder known, with every folder and every reported file below the folder. With a
    // list of events, adds a created event for each file that was not known.
    private async Task AddFolderFilesAsync(ResourceKey folder, List<FileEvent>? fileEvents)
    {
        var walkResult = await _walker.WalkAsync(folder, filter: _matcher);
        if (walkResult.IsFailure)
        {
            // The folder may have gone since the change was delivered. The folder's delete then arrives as a change.
            _logger.LogDebug("Failed to list the folder '{Folder}' for a language server: {Reason}", folder, walkResult.FirstErrorMessage);
            return;
        }

        if (!string.IsNullOrEmpty(folder.Path))
        {
            _knownFolders.Add(folder.Path);
        }

        foreach (var item in walkResult.Value)
        {
            var keyPath = item.Resource.Path;

            if (item.IsFolder)
            {
                _knownFolders.Add(keyPath);
                continue;
            }

            if (!_matcher.IsMatch(keyPath, isFolder: false))
            {
                continue;
            }

            var isNewFile = _knownFiles.Add(keyPath);
            if (isNewFile
                && fileEvents is not null)
            {
                fileEvents.Add(new FileEvent(LspUri.FromPath(item.Path), CreatedType));
            }
        }
    }

    // Forgets a path that has gone, and every known path inside it.
    private void ForgetPath(string keyPath)
    {
        _knownFiles.Remove(keyPath);
        _knownFolders.Remove(keyPath);

        var prefix = keyPath + KeySeparator;
        _knownFiles.RemoveWhere(knownPath => knownPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        _knownFolders.RemoveWhere(knownPath => knownPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private string GetPath(string keyPath)
    {
        var relativePath = keyPath.Replace(KeySeparator, Path.DirectorySeparatorChar);

        return Path.Combine(_projectFolderPath, relativePath);
    }
}
