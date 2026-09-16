using Celbridge.Workshop;

namespace Celbridge.Tools;

/// <summary>
/// Resolves a requested workshop version string to a concrete workshop version number.
/// The string is the latest alias (the highest live workshop version), a workshop version
/// number, or an alias name.
/// </summary>
internal static class WorkshopVersionResolver
{
    /// <summary>
    /// Resolves a requested workshop version string to a concrete workshop version number.
    /// 'latest' selects the highest live workshop version. A workshop version number or alias
    /// dereferences to its target whether or not that version is deleted, leaving the download
    /// or delete as the single authority on liveness.
    /// </summary>
    public static Result<int> Resolve(RemotePackageDetails details, string requestedWorkshopVersion)
    {
        if (string.Equals(requestedWorkshopVersion, WorkshopConstants.LatestAlias, StringComparison.OrdinalIgnoreCase))
        {
            var liveWorkshopVersions = details.WorkshopVersions
                .Where(workshopVersion => !workshopVersion.Deleted)
                .ToList();
            if (liveWorkshopVersions.Count == 0)
            {
                return Result.Fail($"Package '{details.Name}' has no live workshop version available.");
            }

            return liveWorkshopVersions.Max(workshopVersion => workshopVersion.WorkshopVersion);
        }

        if (int.TryParse(requestedWorkshopVersion, out var explicitWorkshopVersion))
        {
            var match = details.WorkshopVersions.FirstOrDefault(workshopVersion => workshopVersion.WorkshopVersion == explicitWorkshopVersion);
            if (match is null)
            {
                return Result.Fail($"Workshop version {explicitWorkshopVersion} not found for package '{details.Name}'.");
            }

            return explicitWorkshopVersion;
        }

        var alias = details.Aliases.FirstOrDefault(packageAlias =>
            string.Equals(packageAlias.Alias, requestedWorkshopVersion, StringComparison.Ordinal));
        if (alias is null)
        {
            return Result.Fail($"'{requestedWorkshopVersion}' is not a workshop version number or a known alias for package '{details.Name}'.");
        }

        var aliasTarget = details.WorkshopVersions.FirstOrDefault(workshopVersion => workshopVersion.WorkshopVersion == alias.WorkshopVersion);
        if (aliasTarget is null)
        {
            return Result.Fail($"Alias '{requestedWorkshopVersion}' points at workshop version {alias.WorkshopVersion}, which does not exist.");
        }

        return alias.WorkshopVersion;
    }
}
