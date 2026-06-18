namespace Celbridge.Settings;

/// <summary>
/// Non-generic view of a setting declaration, so descriptors of differing value
/// types can be enumerated and routed without knowing T.
/// </summary>
public interface ISettingDescriptor
{
    /// <summary>
    /// Stable identifier for the setting, unique across all settings. Doubles as
    /// the storage key and, for Protected settings, as the encryption entropy.
    /// </summary>
    string Key { get; }

    /// <summary>
    /// The storage scope that routes where this setting's bytes land.
    /// </summary>
    SettingScope Scope { get; }
}

/// <summary>
/// A single setting declared once with its key, scope, and default. The setting,
/// not the store, is the primitive: callers reference the descriptor and the
/// service routes storage by scope. A plain class, not a record: descriptors are
/// static singletons referenced by identity, never compared by value.
/// </summary>
public sealed class SettingDescriptor<T> : ISettingDescriptor
    where T : notnull
{
    public SettingDescriptor(string key, SettingScope scope, T defaultValue)
    {
        Key = key;
        Scope = scope;
        DefaultValue = defaultValue;
    }

    public string Key { get; }

    public SettingScope Scope { get; }

    public T DefaultValue { get; }
}
