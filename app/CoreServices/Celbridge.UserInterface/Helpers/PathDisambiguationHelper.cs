namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Helper class to disambiguate file paths with identical filenames by showing
/// the minimum necessary path segments to make them unique.
/// </summary>
public static class PathDisambiguationHelper
{
    /// <summary>
    /// Represents a path being processed for disambiguation.
    /// </summary>
    public sealed class PathEntry
    {
        public string FullPath { get; }
        public string[] PathSegments { get; }
        public int CurrentIndex { get; set; }
        public List<string> DisplaySegments { get; }
        public string FinalDisplayString { get; set; }
        public PathNode? CurrentNode { get; set; }

        public PathEntry(string path)
        {
            FullPath = path;
            
            // Split on both forward and back slashes to handle paths from any platform
            var segments = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            
            // Filter out drive letters (e.g., "C:") and volume roots from path segments
            PathSegments = segments.Where(s => !s.EndsWith(':')).ToArray();
            
            CurrentIndex = PathSegments.Length - 2; // Start from parent directory
            DisplaySegments = new List<string>();
            FinalDisplayString = "";
        }
    }

    /// <summary>
    /// Internal node used for tracking path segment groups during disambiguation.
    /// </summary>
    public sealed class PathNode
    {
        public string Segment { get; }
        public int GroupIndex { get; }

#if DEBUG
        public List<PathNode> NextNodes { get; }
        public List<string> PathIdentifiers { get; }

        public PathNode(string segment, int groupIndex, string initialPathIdentifier)
        {
            Segment = segment;
            GroupIndex = groupIndex;
            NextNodes = new List<PathNode>();
            PathIdentifiers = new List<string> { initialPathIdentifier };
        }
#else
        public PathNode(string segment, int groupIndex, string initialPathIdentifier)
        {
            Segment = segment;
            GroupIndex = groupIndex;
        }
#endif
    }

    /// <summary>
    /// Disambiguates a collection of file paths that share the same filename.
    /// </summary>
    /// <param name="paths">Dictionary mapping unique identifiers to file paths</param>
    /// <returns>Dictionary mapping identifiers to disambiguated display strings</returns>
    public static Dictionary<TKey, string> DisambiguatePaths<TKey>(Dictionary<TKey, string> paths)
        where TKey : notnull
    {
        if (paths.Count == 0)
        {
            return new Dictionary<TKey, string>();
        }

        if (paths.Count == 1)
        {
            // No disambiguation needed for a single path
            var singlePath = paths.First();
            var fileName = Path.GetFileName(singlePath.Value);
            return new Dictionary<TKey, string> { { singlePath.Key, fileName } };
        }

        // Create path entries for processing
        var pathEntries = new Dictionary<TKey, PathEntry>();
        foreach (var kvp in paths)
        {
            pathEntries[kvp.Key] = new PathEntry(kvp.Value);
        }

        // Process the paths to find distinguishing segments
        ProcessPathDisambiguation(pathEntries);

        // Build the final display strings
        var result = new Dictionary<TKey, string>();
        foreach (var kvp in pathEntries)
        {
            result[kvp.Key] = kvp.Value.FinalDisplayString;
        }

        return result;
    }

    private static void ProcessPathDisambiguation<TKey>(Dictionary<TKey, PathEntry> pathEntries)
        where TKey : notnull
    {
        var keys = pathEntries.Keys.ToList();
        int nextGroupIndex = 1;
        bool stillProcessing = true;

        do
        {
            stillProcessing = false;

            // Group entries by their current group index
            var groupToKeys = new Dictionary<int, List<TKey>>();
            foreach (var key in keys)
            {
                var entry = pathEntries[key];
                if (entry.CurrentIndex < 0)
                {
                    continue;
                }

                stillProcessing = true;

                int groupIndex = entry.CurrentNode?.GroupIndex ?? 0;
                if (!groupToKeys.ContainsKey(groupIndex))
                {
                    groupToKeys[groupIndex] = new List<TKey> { key };
                }
                else
                {
                    groupToKeys[groupIndex].Add(key);
                }
            }

            // Process each group
            foreach (var group in groupToKeys)
            {
                var nodeDictionary = new Dictionary<string, PathNode>();

                foreach (var key in group.Value)
                {
                    var entry = pathEntries[key];
                    var segment = entry.PathSegments[entry.CurrentIndex];

                    if (nodeDictionary.ContainsKey(segment))
                    {
#if DEBUG
                        nodeDictionary[segment].PathIdentifiers.Add(key.ToString() ?? "");
#endif
                    }
                    else
                    {
                        nodeDictionary.Add(segment, new PathNode(segment, nextGroupIndex++, key.ToString() ?? ""));
                    }

#if DEBUG
                    if (entry.CurrentNode != null)
                    {
                        if (!nodeDictionary[segment].NextNodes.Contains(entry.CurrentNode))
                        {
                            nodeDictionary[segment].NextNodes.Add(entry.CurrentNode);
                        }
                    }
#endif

                    entry.CurrentNode = nodeDictionary[segment];
                }

                // If we have only one node, paths converge here, so use '...'
                // Otherwise, use the actual segment to show differentiation
                bool useSegment = nodeDictionary.Count > 1;
                foreach (var key in group.Value)
                {
                    var entry = pathEntries[key];
                    if (useSegment)
                    {
                        entry.DisplaySegments.Add(entry.PathSegments[entry.CurrentIndex]);
                    }
                    else
                    {
                        // Add '...' only if we haven't just added one
                        if (entry.DisplaySegments.Count > 0 &&
                            entry.DisplaySegments[^1] != "...")
                        {
                            entry.DisplaySegments.Add("...");
                        }
                    }
                    entry.CurrentIndex--;
                }
            }
        }
        while (stillProcessing);

        // Build final display strings
        foreach (var entry in pathEntries.Values)
        {
            BuildDisplayString(entry);
        }
    }

    /// <summary>
    /// Builds the final display string from the collected display segments.
    /// Uses normalized forward slashes for cross-platform consistency.
    /// </summary>
    private static void BuildDisplayString(PathEntry entry)
    {
        var displaySegments = entry.DisplaySegments;
        displaySegments.Reverse();

        var outputPath = string.Empty;
        foreach (var segment in displaySegments)
        {
            if (outputPath.Length > 0)
            {
                outputPath += '/';
            }
            else if (segment == "...")
            {
                // Skip leading '...'
                continue;
            }
            outputPath += segment;
        }

        // Add filename at the end
        var fileName = entry.PathSegments[^1];
        if (outputPath.Length > 0)
        {
            outputPath += '/';
        }
        outputPath += fileName;

        entry.FinalDisplayString = outputPath;
    }
}
