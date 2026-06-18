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
    /// Resolves a requested version string to a concrete version number. 'latest'
    /// selects the highest live version; a version number or alias dereferences to
    /// its target whether or not that version is deleted, leaving the download or
    /// delete as the single authority on liveness.
    /// </summary>
    public static Result<int> Resolve(RemotePackageDetails details, string requestedVersion)
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

        return alias.Version;
    }
}
