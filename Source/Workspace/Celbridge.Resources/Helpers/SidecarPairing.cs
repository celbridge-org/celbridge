namespace Celbridge.Resources.Helpers;

/// <summary>
/// The files from a list, sorted by the part each plays in sidecar pairing. Files are the keys that are not .cel
/// files. FilesWithSidecars are the files that have a sidecar in the list. Orphans are the sidecars with no file in
/// the list, and InvalidSidecars are the .cel.cel files.
/// </summary>
public sealed record SidecarPairs(
    IReadOnlyList<ResourceKey> Files,
    IReadOnlySet<ResourceKey> FilesWithSidecars,
    IReadOnlyList<ResourceKey> Orphans,
    IReadOnlyList<ResourceKey> InvalidSidecars);

/// <summary>
/// Pairs each sidecar with the file it describes, from a list of files that contains both.
/// </summary>
public static class SidecarPairing
{
    private const string InvalidSidecarExtension = SidecarFile.Extension + SidecarFile.Extension;

    /// <summary>
    /// Pairs a file with a sidecar when the sidecar's key is the key the sidecar service builds for the file. A
    /// .cel.cel file never pairs.
    /// </summary>
    public static SidecarPairs Pair(IEnumerable<ResourceKey> fileKeys, ISidecarService sidecarService)
    {
        var files = new List<ResourceKey>();
        var sidecars = new List<ResourceKey>();
        var invalidSidecars = new List<ResourceKey>();

        foreach (var fileKey in fileKeys)
        {
            if (fileKey.ResourceName.EndsWith(InvalidSidecarExtension, StringComparison.OrdinalIgnoreCase))
            {
                invalidSidecars.Add(fileKey);
            }
            else if (sidecarService.IsSidecarKey(fileKey))
            {
                sidecars.Add(fileKey);
            }
            else
            {
                files.Add(fileKey);
            }
        }

        var unpairedSidecars = new HashSet<ResourceKey>(sidecars);
        var filesWithSidecars = new HashSet<ResourceKey>();
        foreach (var file in files)
        {
            var sidecarKeyResult = sidecarService.GetSidecarKey(file);
            if (sidecarKeyResult.IsFailure)
            {
                continue;
            }
            var sidecarKey = sidecarKeyResult.Value;

            if (unpairedSidecars.Remove(sidecarKey))
            {
                filesWithSidecars.Add(file);
            }
        }

        var orphans = sidecars
            .Where(unpairedSidecars.Contains)
            .ToList();

        return new SidecarPairs(files, filesWithSidecars, orphans, invalidSidecars);
    }
}
