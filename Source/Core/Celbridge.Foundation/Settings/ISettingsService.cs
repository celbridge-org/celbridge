namespace Celbridge.Settings;

/// <summary>
/// The single entry point for reading and writing settings. Storage is routed by
/// the descriptor's scope; callers see one synchronous, typed API regardless of
/// where the bytes land. Reads are write-through against an in-memory view, so a
/// Get immediately after a Set returns the value just written.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Returns true when the scope's backend is available on this platform.
    /// Protected requires a platform credential store; Workspace requires a
    /// loaded project.
    /// </summary>
    bool IsScopeAvailable(SettingScope scope);

    /// <summary>
    /// Gets the setting's stored value, or its descriptor default when no value
    /// is stored or the stored value cannot be read.
    /// </summary>
    T Get<T>(SettingDescriptor<T> setting) where T : notnull;

    /// <summary>
    /// Stores a new value for the setting.
    /// </summary>
    void Set<T>(SettingDescriptor<T> setting, T value) where T : notnull;

    /// <summary>
    /// Returns true when the setting has a stored value, false when reads would
    /// return the default. Cheap: does not decrypt Protected values.
    /// </summary>
    bool IsConfigured<T>(SettingDescriptor<T> setting) where T : notnull;

    /// <summary>
    /// Gets the setting's stored value, surfacing backend errors as a Result
    /// rather than falling back to the default. Useful for Protected settings,
    /// where unprotection can genuinely fail.
    /// </summary>
    Result<T> TryGet<T>(SettingDescriptor<T> setting) where T : notnull;

    /// <summary>
    /// Resets the setting to its default by removing the stored value. Takes the
    /// non-generic descriptor so callers can reset a heterogeneous set of settings
    /// in a loop; reset needs only the key and scope.
    /// </summary>
    void Reset(ISettingDescriptor setting);
}
