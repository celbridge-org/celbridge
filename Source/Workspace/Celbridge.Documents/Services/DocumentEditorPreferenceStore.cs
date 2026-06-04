using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Documents.Services;

/// <summary>
/// Reads and writes the user's preferred editor for a file. Knows two
/// preference sources: the per-file sidecar 'editor' field (set by
/// "Open with...") and the per-extension workspace setting. The sidecar
/// preference takes precedence; resolution stops at the first non-empty value.
/// </summary>
public class DocumentEditorPreferenceStore
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILogger<DocumentEditorPreferenceStore> _logger;

    public DocumentEditorPreferenceStore(
        IWorkspaceWrapper workspaceWrapper,
        ILogger<DocumentEditorPreferenceStore> logger)
    {
        _workspaceWrapper = workspaceWrapper;
        _logger = logger;
    }

    /// <summary>
    /// Returns the per-extension workspace preference, or Empty when no
    /// preference is stored or the stored value does not parse.
    /// </summary>
    public async Task<DocumentEditorId> GetExtensionPreferenceAsync(string extension)
    {
        var workspaceSettings = _workspaceWrapper.WorkspaceService.WorkspaceSettings;
        Guard.IsNotNull(workspaceSettings);

        var preferenceKey = DocumentConstants.GetEditorPreferenceKey(extension);
        var preferredId = await workspaceSettings.GetPropertyAsync<string>(preferenceKey);

        // TryParse handles empty/null/malformed strings; callers are responsible
        // for checking whether the returned id still maps to a registered editor.
        if (string.IsNullOrEmpty(preferredId)
            || !DocumentEditorId.TryParse(preferredId, out var editorId))
        {
            return DocumentEditorId.Empty;
        }

        return editorId;
    }

    /// <summary>
    /// Stores the user's preferred editor for an extension. Pass Empty to clear
    /// the preference.
    /// </summary>
    public async Task SetExtensionPreferenceAsync(string extension, DocumentEditorId editorId)
    {
        var workspaceSettings = _workspaceWrapper.WorkspaceService.WorkspaceSettings;
        Guard.IsNotNull(workspaceSettings);

        var preferenceKey = DocumentConstants.GetEditorPreferenceKey(extension);

        if (editorId.IsEmpty)
        {
            await workspaceSettings.DeletePropertyAsync(preferenceKey);
            return;
        }

        await workspaceSettings.SetPropertyAsync(preferenceKey, editorId.ToString());
    }

    /// <summary>
    /// Reads the resource's sidecar (if any) and returns its 'editor' field as
    /// a DocumentEditorId. Returns success with Empty when no sidecar exists,
    /// the sidecar has no 'editor' field, or the field value does not parse.
    /// Returns failure only on unexpected sidecar service errors so callers can
    /// fall through gracefully on success.
    /// </summary>
    public async Task<Result<DocumentEditorId>> GetSidecarPreferenceAsync(ResourceKey fileResource)
    {
        var sidecarService = _workspaceWrapper.WorkspaceService.ResourceService.Sidecars;
        if (sidecarService.IsSidecarKey(fileResource))
        {
            // The sidecar file itself does not have a sidecar pairing of its
            // own; nothing to consult.
            return DocumentEditorId.Empty;
        }

        var readResult = await sidecarService.ReadAsync(fileResource);
        if (readResult.IsFailure)
        {
            return Result<DocumentEditorId>.Fail($"Failed to read sidecar for '{fileResource}'")
                .WithErrors(readResult);
        }

        var sidecar = readResult.Value;
        if (sidecar.Outcome != SidecarReadOutcome.Healthy
            || sidecar.Content is null)
        {
            return DocumentEditorId.Empty;
        }

        if (!sidecar.Content.Frontmatter.TryGetValue(DocumentConstants.SidecarEditorFieldName, out var editorValue)
            || editorValue is not string editorIdString
            || string.IsNullOrWhiteSpace(editorIdString))
        {
            return DocumentEditorId.Empty;
        }

        if (!DocumentEditorId.TryParse(editorIdString, out var editorId))
        {
            _logger.LogDebug($"Sidecar for '{fileResource}' has malformed editor value '{editorIdString}'");
            return DocumentEditorId.Empty;
        }

        return editorId;
    }

    /// <summary>
    /// Returns the editor that should open the given file: the sidecar 'editor'
    /// field when set, otherwise the per-extension workspace preference, or
    /// Empty when neither source has a preference.
    /// </summary>
    public async Task<DocumentEditorId> GetPreferredEditorAsync(ResourceKey fileResource)
    {
        var sidecarPreferenceResult = await GetSidecarPreferenceAsync(fileResource);
        if (sidecarPreferenceResult.IsSuccess
            && !sidecarPreferenceResult.Value.IsEmpty)
        {
            return sidecarPreferenceResult.Value;
        }

        var extension = Path.GetExtension(fileResource.ToString()).ToLowerInvariant();
        var extensionPreference = await GetExtensionPreferenceAsync(extension);
        return extensionPreference;
    }
}
