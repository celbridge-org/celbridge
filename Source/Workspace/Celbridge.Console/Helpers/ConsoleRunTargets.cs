using Celbridge.UserInterface.Helpers;

namespace Celbridge.Console.Helpers;

/// <summary>
/// A session considered for a run: its identity, the absolute path of its .console file, its effective
/// runners, and whether those runners are stale (a client connection was bound and then lost, so the REPL
/// they target has exited back to the shell prompt).
/// </summary>
public sealed record ConsoleRunCandidate(
    Guid SessionId,
    ResourceKey ResourceKey,
    string FilePath,
    IReadOnlyList<ConsoleRunner> Runners,
    bool HasStaleRunners);

/// <summary>
/// Selects which console sessions can run a file, and which runner each would use.
/// </summary>
public static class ConsoleRunTargets
{
    /// <summary>
    /// Returns the candidates whose runners cover a file extension, sorted by display name, skipping any
    /// whose runners are stale.
    /// </summary>
    public static IReadOnlyList<ConsoleRunTarget> Resolve(IEnumerable<ConsoleRunCandidate> candidates, string fileExtension)
    {
        var matches = new List<ConsoleRunCandidate>();
        foreach (var candidate in candidates)
        {
            if (candidate.HasStaleRunners)
            {
                continue;
            }

            if (FindRunner(candidate.Runners, fileExtension) is null)
            {
                continue;
            }

            matches.Add(candidate);
        }

        var displayNames = BuildDisplayNames(matches);

        var targets = new List<ConsoleRunTarget>();
        foreach (var candidate in matches)
        {
            var target = new ConsoleRunTarget(candidate.SessionId, candidate.ResourceKey, displayNames[candidate.SessionId]);
            targets.Add(target);
        }

        // Sorted by the name the menu shows, so the order is one the user can predict. The resource key
        // breaks the remaining ties, since the sessions arrive in dictionary order.
        var ordered = targets
            .OrderBy(target => target.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(target => target.ResourceKey.Path, StringComparer.Ordinal)
            .ToList();

        return ordered;
    }

    // File names alone, widened to include parent folders only where two targets would otherwise read the
    // same. This is the disambiguation the document tabs use, fed the absolute paths they are fed, so a
    // console reads identically in the tab strip and here. Project-relative paths would not do: a console
    // in the project root has no parent segment for the helper to widen into.
    private static Dictionary<Guid, string> BuildDisplayNames(IReadOnlyList<ConsoleRunCandidate> candidates)
    {
        var candidatesByFileName = new Dictionary<string, List<ConsoleRunCandidate>>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var fileName = candidate.ResourceKey.ResourceName;
            if (!candidatesByFileName.TryGetValue(fileName, out var group))
            {
                group = new List<ConsoleRunCandidate>();
                candidatesByFileName[fileName] = group;
            }

            group.Add(candidate);
        }

        var displayNames = new Dictionary<Guid, string>();
        foreach (var group in candidatesByFileName.Values)
        {
            var pathsBySessionId = new Dictionary<Guid, string>();
            foreach (var candidate in group)
            {
                pathsBySessionId[candidate.SessionId] = candidate.FilePath;
            }

            // A single-entry group comes back as the bare file name.
            var disambiguated = PathDisambiguationHelper.DisambiguatePaths(pathsBySessionId);
            foreach (var entry in disambiguated)
            {
                displayNames[entry.Key] = entry.Value;
            }
        }

        return displayNames;
    }

    /// <summary>
    /// Returns the runner that handles a file extension, or null if none does.
    /// </summary>
    public static ConsoleRunner? FindRunner(IReadOnlyList<ConsoleRunner> runners, string fileExtension)
    {
        foreach (var runner in runners)
        {
            foreach (var extension in runner.FileExtensions)
            {
                if (string.Equals(extension, fileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    return runner;
                }
            }
        }

        return null;
    }
}
