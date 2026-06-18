namespace Celbridge.Settings;

/// <summary>
/// Where a setting's bytes are stored. The scope is the only thing that differs
/// between settings; everything else (typed reads, writes, change notifications)
/// is common. Encoding "encrypted at rest" as its own scope rather than a flag
/// keeps the valid combinations exhaustive: a workspace secret bound to one user
/// and machine cannot be declared by accident.
/// </summary>
public enum SettingScope
{
    /// <summary>
    /// User and installation level. Persists in user-config storage and is
    /// shared across every project the user opens.
    /// </summary>
    Application,

    /// <summary>
    /// Per-project. Persists alongside the project files so each project
    /// remembers its own value.
    /// </summary>
    Workspace,

    /// <summary>
    /// User and installation level, encrypted at rest (DPAPI on Windows). For
    /// secrets that must never travel with a project folder.
    /// </summary>
    Protected,
}
