namespace Celbridge.Documents.Views;

/// <summary>
/// What was deferred for an editor page until it reported its initial load.
/// </summary>
internal sealed record DeferredPageUpdates(string? Location, string? EditorStateJson, bool Rename, bool Reload);

/// <summary>
/// The web page an editor runs in, from the navigation that loads it until it is replaced or torn down.
/// The page takes the document's notifications and requests only once it reports its initial load, which
/// it does after registering its handlers, so anything that arrives before then is deferred here and handed
/// back when it loads. Used on the UI thread, where both the page's calls and the editor's events arrive.
/// </summary>
internal sealed class EditorPage
{
    // The address the page loaded at, without its query or fragment. A navigation back to it replaces the
    // page, while a frame inside the page loads at an address of its own.
    private string? _address;

    private string? _deferredLocation;
    private string? _deferredEditorStateJson;
    private bool _isRenameDeferred;
    private bool _isReloadDeferred;

    /// <summary>
    /// Whether the page has reported its initial load since it last navigated.
    /// </summary>
    public bool IsLoaded { get; private set; }

    /// <summary>
    /// The document the page was last told it shows.
    /// </summary>
    public ResourceKey Resource { get; private set; } = ResourceKey.Empty;

    /// <summary>
    /// Records the document the page is being told it shows, as its metadata is sent.
    /// </summary>
    public void SetResource(ResourceKey resource)
    {
        Resource = resource;
    }

    /// <summary>
    /// Called for each navigation the editor allows. The first is the page's own. A later one back to the
    /// same address replaces the page with one that has not loaded, and one to any other address is a frame
    /// inside the page, which leaves it as it is.
    /// </summary>
    public void OnNavigating(string uri)
    {
        var address = ToPageAddress(uri);
        _address ??= address;

        if (address == _address)
        {
            IsLoaded = false;
        }
    }

    /// <summary>
    /// Defers a location to navigate to until the page loads. The latest one wins. Returns false when the
    /// page is ready to take it now.
    /// </summary>
    public bool TryDeferLocation(string location)
    {
        if (IsLoaded)
        {
            return false;
        }

        _deferredLocation = location;
        return true;
    }

    /// <summary>
    /// Defers editor state to restore until the page loads. The latest one wins. Returns false when the page
    /// is ready to take it now.
    /// </summary>
    public bool TryDeferEditorState(string editorStateJson)
    {
        if (IsLoaded)
        {
            return false;
        }

        _deferredEditorStateJson = editorStateJson;
        return true;
    }

    /// <summary>
    /// Defers a rename until the page loads. Returns false when the page is ready to take it now.
    /// </summary>
    public bool TryDeferRename()
    {
        if (IsLoaded)
        {
            return false;
        }

        _isRenameDeferred = true;
        return true;
    }

    /// <summary>
    /// Defers an external reload until the page loads. Returns false when the page is ready to take it now.
    /// </summary>
    public bool TryDeferReload()
    {
        if (IsLoaded)
        {
            return false;
        }

        _isReloadDeferred = true;
        return true;
    }

    /// <summary>
    /// Called when the page reports its initial load. Marks it loaded and hands back what was deferred for
    /// it, which is then no longer deferred. A deferred rename is handed back only when the page was told of
    /// another document than the one it now shows.
    /// </summary>
    public DeferredPageUpdates OnLoaded(ResourceKey currentResource)
    {
        IsLoaded = true;

        var deferred = new DeferredPageUpdates(
            _deferredLocation,
            _deferredEditorStateJson,
            Rename: _isRenameDeferred && Resource != currentResource,
            Reload: _isReloadDeferred);

        _deferredLocation = null;
        _deferredEditorStateJson = null;
        _isRenameDeferred = false;
        _isReloadDeferred = false;

        return deferred;
    }

    /// <summary>
    /// Forgets the page and everything deferred for it, as when the editor is torn down.
    /// </summary>
    public void Reset()
    {
        _address = null;
        _deferredLocation = null;
        _deferredEditorStateJson = null;
        _isRenameDeferred = false;
        _isReloadDeferred = false;
        IsLoaded = false;
        Resource = ResourceKey.Empty;
    }

    // The address that identifies a page. A reload's new query and a fragment do not change which page it is.
    private static string ToPageAddress(string uri)
    {
        var endIndex = uri.IndexOfAny(['?', '#']);
        if (endIndex < 0)
        {
            return uri;
        }

        return uri[..endIndex];
    }
}
