namespace Celbridge.Packages;

/// <summary>
/// Reasons a package can fail to load or register during discovery.
/// </summary>
public enum PackageLoadFailureReason
{
    /// <summary>
    /// The package manifest is missing, cannot be parsed, is missing required
    /// fields, or has an invalid id format.
    /// </summary>
    InvalidManifest,

    /// <summary>
    /// A project package tried to claim a package id under the reserved
    /// "celbridge." namespace that is restricted to first-party bundled packages.
    /// </summary>
    ReservedIdPrefix,

    /// <summary>
    /// A project package used a dotted id but no namespace registry exists yet
    /// to validate the prefix. Only flat ids are currently permitted for
    /// project packages.
    /// </summary>
    UnregisteredNamespace,

    /// <summary>
    /// The package id collides with another loaded package. Covers bundled vs
    /// bundled, project vs bundled, and project vs project conflicts.
    /// </summary>
    DuplicateId,
}

/// <summary>
/// Describes a single package that failed to load or register.
/// </summary>
public sealed record PackageLoadFailure
{
    public string Folder { get; init; } = string.Empty;
    public string? PackageId { get; init; }
    public PackageLoadFailureReason Reason { get; init; }
}

/// <summary>
/// Outcome of a single package discovery pass: how many bundled and project
/// packages loaded successfully, and which packages failed along with the
/// reason for each failure.
/// </summary>
public sealed record PackageDiscoveryReport
{
    public int BundledPackageCount { get; init; }
    public int ProjectPackageCount { get; init; }
    public IReadOnlyList<PackageLoadFailure> Failures { get; init; } = Array.Empty<PackageLoadFailure>();
}
