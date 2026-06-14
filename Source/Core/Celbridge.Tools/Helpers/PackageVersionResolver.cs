using Celbridge.Packages;

namespace Celbridge.Tools;

/// <summary>
/// Resolves a requested version string to a concrete workshop version number.
/// The string is the latest alias (the highest live version), a version number,
/// or an alias name.
/// </summary>
internal static class PackageVersionResolver
{
    /// <summary>
    /// Resolves a version for install. A deleted version cannot be downloaded,
    /// so a deleted target is rejected with a clear error rather than resolved.
    /// </summary>
    public static Result<int> ResolveForInstall(RemotePackageDetails details, string requestedVersion)
    {
        return Resolve(details, requestedVersion, rejectDeleted: true);
    }

    /// <summary>
    /// Resolves a version for delete. A deleted target is not pre-rejected here.
    /// The client reports the already-deleted state instead. The latest alias
    /// still selects the highest live version, never a dead one.
    /// </summary>
    public static Result<int> ResolveForDelete(RemotePackageDetails details, string requestedVersion)
    {
        return Resolve(details, requestedVersion, rejectDeleted: false);
    }

    private static Result<int> Resolve(RemotePackageDetails details, string requestedVersion, bool rejectDeleted)
    {
        if (string.Equals(requestedVersion, PackageConstants.LatestAlias, StringComparison.OrdinalIgnoreCase))
        {
            var liveVersions = details.Versions
                .Where(packageVersion => !packageVersion.Deleted)
                .ToList();
            if (liveVersions.Count == 0)
            {
                return Result.Fail($"Package '{details.Name}' has no live version available.");
            }

            return liveVersions.Max(packageVersion => packageVersion.Version);
        }

        if (int.TryParse(requestedVersion, out var explicitVersion))
        {
            var match = details.Versions.FirstOrDefault(packageVersion => packageVersion.Version == explicitVersion);
            if (match is null)
            {
                return Result.Fail($"Version {explicitVersion} not found for package '{details.Name}'.");
            }
            if (rejectDeleted
                && match.Deleted)
            {
                return Result.Fail($"Version {explicitVersion} of package '{details.Name}' has been deleted and cannot be installed.");
            }

            return explicitVersion;
        }

        var alias = details.Aliases.FirstOrDefault(packageAlias =>
            string.Equals(packageAlias.Alias, requestedVersion, StringComparison.Ordinal));
        if (alias is null)
        {
            return Result.Fail($"'{requestedVersion}' is not a version number or a known alias for package '{details.Name}'.");
        }

        var aliasTarget = details.Versions.FirstOrDefault(packageVersion => packageVersion.Version == alias.Version);
        if (aliasTarget is null)
        {
            return Result.Fail($"Alias '{requestedVersion}' points at version {alias.Version}, which does not exist.");
        }
        if (rejectDeleted
            && aliasTarget.Deleted)
        {
            return Result.Fail($"Alias '{requestedVersion}' points at version {alias.Version}, which has been deleted.");
        }

        return alias.Version;
    }
}
