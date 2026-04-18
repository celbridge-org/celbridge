using Celbridge.Host;
using Celbridge.Logging;

namespace Celbridge.Code.Views;

/// <summary>
/// Handles IHostDocument RPC methods for the code editor.
/// Manages document initialization, loading, saving, and lifecycle notifications.
/// </summary>
internal sealed class CodeEditorDocumentHandler : IHostDocument
{
    private readonly ILogger _logger;
    private readonly CodeEditorState _state;
    private readonly Action _onDocumentChanged;

    /// <summary>
    /// Set by the owning control before navigating to Monaco.
    /// Completed when the JS client signals it is ready.
    /// </summary>
    internal TaskCompletionSource? ClientReadyTcs { get; set; }

    /// <summary>
    /// Set by the owning control before initializing the editor.
    /// Completed when Monaco signals content is loaded.
    /// </summary>
    internal TaskCompletionSource? ContentLoadedTcs { get; set; }

    /// <summary>
    /// Set by the owning control when requesting content via GetContentAsync.
    /// Completed when SaveAsync is called back by Monaco with the current content.
    /// </summary>
    internal TaskCompletionSource<string>? GetContentTcs { get; set; }

    /// <summary>
    /// Raised when the JS client requests a content reload via the document/load RPC.
    /// This fires before the JS sets the content in the editor.
    /// </summary>
    internal event Action? ContentLoadRequested;

    /// <summary>
    /// Raised every time the editor signals it has finished loading (or reloading) content.
    /// The reason argument distinguishes the initial load from an external-change reload.
    /// </summary>
    internal event Action<ContentLoadedReason>? ContentLoaded;

    public CodeEditorDocumentHandler(
        ILogger logger,
        CodeEditorState state,
        Action onDocumentChanged)
    {
        _logger = logger;
        _state = state;
        _onDocumentChanged = onDocumentChanged;
    }

    public async Task<InitializeResult> InitializeAsync(string protocolVersion)
    {
        DocumentRpcMethods.ValidateProtocolVersion(protocolVersion);

        var metadata = CreateMetadata();

        await Task.CompletedTask;

        return new InitializeResult(_state.Content, metadata);
    }

    public async Task<LoadResult> LoadAsync()
    {
        var contentLoader = _state.ContentLoader;

        if (contentLoader is not null)
        {
            _state.Content = await contentLoader();
        }
        else
        {
            _logger.LogWarning($"LoadAsync has no ContentLoader for file: {_state.ResourceKey}");
        }

        ContentLoadRequested?.Invoke();

        var metadata = CreateMetadata();

        return new LoadResult(_state.Content, metadata);
    }

    public async Task<SaveResult> SaveAsync(string content)
    {
        _state.Content = content;

        GetContentTcs?.TrySetResult(content);

        await Task.CompletedTask;

        return new SaveResult(true);
    }

    public void OnDocumentChanged()
    {
        _onDocumentChanged();
    }

    public void OnClientReady()
    {
        ClientReadyTcs?.TrySetResult();
    }

    public void OnContentLoaded(ContentLoadedReason reason = ContentLoadedReason.Initial)
    {
        // TrySetResult is idempotent: the TCS only fires on the initial load (the first call),
        // subsequent reload notifications are no-ops at this point.
        ContentLoadedTcs?.TrySetResult();

        ContentLoaded?.Invoke(reason);
    }

    private DocumentMetadata CreateMetadata()
    {
        // Monaco does not ship celbridge.js localization files; sending an empty
        // locale tells the client not to attempt a /localization/{locale}.json fetch.
        return new DocumentMetadata(
            _state.FilePath,
            _state.ResourceKey,
            Path.GetFileName(_state.FilePath),
            string.Empty);
    }
}
