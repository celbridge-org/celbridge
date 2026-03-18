namespace Celbridge.Extensions;

/// <summary>
/// Extension identity, permissions, and hosting information.
/// Shared across all contributions from the same extension.
/// </summary>
public partial record ExtensionInfo
{
    /// <summary>
    /// Unique identifier for the extension (e.g., "celbridge.notes").
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Display name of the extension (from extension.toml).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Optional feature flag. When set, all contributions are disabled if this feature is off.
    /// </summary>
    public string? FeatureFlag { get; init; }

    /// <summary>
    /// The folder containing the extension (set during loading, not from TOML).
    /// </summary>
    public string ExtensionFolder { get; init; } = string.Empty;

    /// <summary>
    /// A unique virtual host name for this extension's assets (set during loading, not from TOML).
    /// </summary>
    public string HostName { get; init; } = string.Empty;
}
