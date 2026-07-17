using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Documents.Services;

/// <summary>
/// Reads and writes the user's preferred editor for a file. Knows two
/// preference sources: the per-file sidecar '_editor' field (set by
/// "Open with...") and the per-extension workspace setting. The sidecar
/// preference takes precedence. Resolution stops at the first non-empty value.
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
    public async Task<EditorInstanceId> GetExtensionPreferenceAsync(string extension)
    {
        var propertyBag = _workspaceWrapper.WorkspaceService.WorkspaceSettings.PropertyBag;
        Guard.IsNotNull(propertyBag);

        var preferenceKey = DocumentConstants.GetEditorPreferenceKey(extension);
        var preferredId = await propertyBag.GetPropertyAsync<string>(preferenceKey);

        // Callers must check that the returned id still maps to a registered editor.
        if (string.IsNullOrEmpty(preferredId)
            || !EditorInstanceId.TryParse(preferredId, out var editorId))
        {
            return EditorInstanceId.Empty;
        }

        return editorId;
    }

    /// <summary>
    /// Stores the user's preferred editor for an extension. Pass Empty to clear
    /// the preference.
    /// </summary>
    public async Task SetExtensionPreferenceAsync(string extension, EditorInstanceId editorId)
    {
        var propertyBag = _workspaceWrapper.WorkspaceService.WorkspaceSettings.PropertyBag;
        Guard.IsNotNull(propertyBag);

        var preferenceKey = DocumentConstants.GetEditorPreferenceKey(extension);

        if (editorId.IsEmpty)
        {
            await propertyBag.DeletePropertyAsync(preferenceKey);
            return;
        }

        await propertyBag.SetPropertyAsync(preferenceKey, editorId.ToString());
    }

    /// <summary>
    /// Reads the resource's sidecar (if any) and returns its '_editor' field as
    /// an EditorInstanceId. Returns success with Empty when no sidecar exists,
    /// the sidecar has no '_editor' field, or the field value does not parse.
    /// Returns failure only on unexpected sidecar service errors.
    /// </summary>
    public async Task<Result<EditorInstanceId>> GetSidecarPreferenceAsync(ResourceKey fileResource)
    {
        var sidecarService = _workspaceWrapper.WorkspaceService.ResourceService.Sidecars;
        if (sidecarService.IsSidecarKey(fileResource))
        {
            // The sidecar file itself does not have a sidecar pairing of its
            // own, so there is nothing to consult.
            return EditorInstanceId.Empty;
        }

        var readResult = await sidecarService.ReadAsync(fileResource);
        if (readResult.IsFailure)
        {
            return Result<EditorInstanceId>.Fail($"Failed to read sidecar for '{fileResource}'")
                .WithErrors(readResult);
        }

        var sidecar = readResult.Value;
        if (sidecar.Outcome != SidecarReadOutcome.Healthy
            || sidecar.Content is null)
        {
            return EditorInstanceId.Empty;
        }

        if (!sidecar.Content.Fields.TryGetValue(SidecarFieldNames.Editor, out var editorValue)
            || editorValue is not string editorIdString
            || string.IsNullOrWhiteSpace(editorIdString))
        {
            return EditorInstanceId.Empty;
        }

        if (!EditorInstanceId.TryParse(editorIdString, out var editorId))
        {
            _logger.LogDebug($"Sidecar for '{fileResource}' has malformed editor value '{editorIdString}'");
            return EditorInstanceId.Empty;
        }

        return editorId;
    }

    /// <summary>
    /// Returns the editor that should open the given file: the sidecar '_editor'
    /// field when set, otherwise the per-extension workspace preference, or
    /// Empty when neither source has a preference.
    /// </summary>
    public async Task<EditorInstanceId> GetPreferredEditorAsync(ResourceKey fileResource)
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
