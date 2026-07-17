namespace Celbridge.Packages;

/// <summary>
/// Reasons a package can fail to load or register during discovery.
/// </summary>
public enum PackageLoadFailureReason
{
    /// <summary>
    /// The package manifest is missing, cannot be parsed, is missing required
    /// fields, or has an invalid name format.
    /// </summary>
    InvalidManifest,

    /// <summary>
    /// A project package tried to claim a name under the reserved "celbridge."
    /// namespace that is restricted to first-party bundled packages.
    /// </summary>
    ReservedNamePrefix,

    /// <summary>
    /// A project package used a dotted name but no namespace registry exists yet
    /// to validate the prefix. Only flat names are currently permitted for
    /// project packages.
    /// </summary>
    UnregisteredNamespace,

    /// <summary>
    /// The package name collides with another loaded package. Covers bundled vs
    /// bundled, project vs bundled, and project vs project conflicts.
    /// </summary>
    DuplicateName,

    /// <summary>
    /// A package's document-type registration declares a file extension that
    /// lies in the reserved .cel sidecar namespace.
    /// </summary>
    ReservedExtension,
}

/// <summary>
/// Describes a single package that failed to load or register.
/// </summary>
public sealed record PackageLoadFailure
{
    public string Folder { get; init; } = string.Empty;
    public string? PackageName { get; init; }
    public PackageLoadFailureReason Reason { get; init; }

    /// <summary>
    /// Optional error detail explaining the failure, carried so diagnostic
    /// surfaces can show the cause without consulting the application log.
    /// Null when the reason alone describes the failure.
    /// </summary>
    public string? Detail { get; init; }
}

/// <summary>
/// Describes a declared editor instance that was skipped, or that loaded with some of its
/// configuration dropped.
/// </summary>
public sealed record EditorInstanceLoadFailure
{
    /// <summary>
    /// The instance id from the project config table.
    /// </summary>
    public string InstanceId { get; init; } = string.Empty;

    /// <summary>
    /// The reason the instance was skipped or degraded.
    /// </summary>
    public string Detail { get; init; } = string.Empty;
}

/// <summary>
/// Outcome of a single package discovery pass: how many bundled and project
/// packages loaded successfully, which packages failed along with the reason
/// for each failure, and how the project's declared instances resolved.
/// </summary>
public sealed record PackageDiscoveryReport
{
    public int BundledPackageCount { get; init; }
    public int ProjectPackageCount { get; init; }
    public IReadOnlyList<PackageLoadFailure> Failures { get; init; } = Array.Empty<PackageLoadFailure>();

    /// <summary>
    /// Number of declared editor instances that resolved successfully.
    /// </summary>
    public int EditorInstanceCount { get; init; }

    /// <summary>
    /// Declared instances that were skipped (unknown package, inactive package, or unknown
    /// contribution).
    /// </summary>
    public IReadOnlyList<EditorInstanceLoadFailure> EditorInstanceFailures { get; init; } = Array.Empty<EditorInstanceLoadFailure>();

    /// <summary>
    /// Declared instances that loaded with invalid configuration keys dropped.
    /// </summary>
    public IReadOnlyList<EditorInstanceLoadFailure> EditorInstanceWarnings { get; init; } = Array.Empty<EditorInstanceLoadFailure>();
}
