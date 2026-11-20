using Celbridge.LanguageServers.Services;

namespace Celbridge.Tests.LanguageServers;

/// <summary>
/// A language server session's workspace, for tests. It records each request, and answers with the
/// values a test sets.
/// </summary>
internal sealed class FakeLanguageServerWorkspace : ILanguageServerWorkspace
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    private readonly TaskCompletionSource<IReadOnlyList<FileEdits>> _appliedEdit =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource<(string FilePath, DocumentRange? Selection)> _shownDocument =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// The result of every edit.
    /// </summary>
    public Result ApplyResult { get; set; } = Result.Ok();

    /// <summary>
    /// The answer to every request to open a document.
    /// </summary>
    public bool ShowResult { get; set; } = true;

    /// <summary>
    /// The stamp and the text of each file, by absolute path.
    /// </summary>
    public Dictionary<string, (string Stamp, string Text)> Files { get; } = new();

    /// <summary>
    /// The number of files that the workspace has read.
    /// </summary>
    public int ReadCount { get; private set; }

    /// <summary>
    /// Returns the session's first edit.
    /// </summary>
    public Task<IReadOnlyList<FileEdits>> WaitForEditAsync()
    {
        return _appliedEdit.Task.WaitAsync(WaitTimeout);
    }

    /// <summary>
    /// Returns the first document opened by the session, with the selection.
    /// </summary>
    public Task<(string FilePath, DocumentRange? Selection)> WaitForShownDocumentAsync()
    {
        return _shownDocument.Task.WaitAsync(WaitTimeout);
    }

    public async Task<Result> ApplyEditAsync(IReadOnlyList<FileEdits> edits)
    {
        await Task.CompletedTask;

        _appliedEdit.TrySetResult(edits);

        return ApplyResult;
    }

    public async Task<bool> ShowDocumentAsync(string filePath, DocumentRange? selection)
    {
        await Task.CompletedTask;

        _shownDocument.TrySetResult((filePath, selection));

        return ShowResult;
    }

    public async Task<string> GetFileStampAsync(string filePath)
    {
        await Task.CompletedTask;

        if (Files.TryGetValue(filePath, out var file))
        {
            return file.Stamp;
        }

        return string.Empty;
    }

    public async Task<Result<string>> ReadFileTextAsync(string filePath)
    {
        await Task.CompletedTask;

        ReadCount++;

        if (Files.TryGetValue(filePath, out var file))
        {
            return file.Text;
        }

        return Result.Fail($"File not found: '{filePath}'");
    }
}
