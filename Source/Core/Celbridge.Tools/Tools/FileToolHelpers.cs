using System.Text.RegularExpressions;

namespace Celbridge.Tools;

/// <summary>
/// Pure static helpers for file tool operations. Extracted for testability.
/// </summary>
public static class FileToolHelpers
{
    /// <summary>
    /// Recursively builds a tree of folder and file nodes from a folder resource.
    /// Returns null when glob filtering is active and the folder contains no
    /// matching descendants, allowing the caller to prune empty branches.
    /// </summary>
    /// <param name="folder">The folder resource to build from.</param>
    /// <param name="remainingDepth">How many more levels to expand. Zero produces a leaf node marked truncated if the folder has children.</param>
    /// <param name="globRegex">Optional compiled glob regex to filter files by name. Folders are kept if they have any matching descendants.</param>
    /// <param name="typeFilter">Optional type filter: "file" to include only file nodes, "folder" to include only folder nodes. Empty or unrecognised values include both.</param>
    public static TreeFolderNode? BuildTree(IFolderResource folder, int remainingDepth, Regex? globRegex, string typeFilter)
    {
        var children = new List<object>();
        bool isTruncated = false;

        if (remainingDepth > 0)
        {
            var showFiles = !string.Equals(typeFilter, "folder", StringComparison.OrdinalIgnoreCase);
            var showFolders = !string.Equals(typeFilter, "file", StringComparison.OrdinalIgnoreCase);

            foreach (var child in folder.Children)
            {
                if (child is IFolderResource childFolder)
                {
                    var childNode = BuildTree(childFolder, remainingDepth - 1, globRegex, typeFilter);
                    if (childNode is not null && showFolders)
                    {
                        children.Add(childNode);
                    }
                }
                else if (showFiles)
                {
                    if (globRegex is not null && !globRegex.IsMatch(child.Name))
                    {
                        continue;
                    }
                    children.Add(new TreeFileNode(child.Name, "file"));
                }
            }
        }
        else if (folder.Children.Any())
        {
            isTruncated = true;
        }

        if (globRegex is not null && !isTruncated && children.Count == 0 && remainingDepth > 0)
        {
            return null;
        }

        if (isTruncated)
        {
            return new TreeFolderNode(folder.Name, "folder", children, Truncated: true);
        }

        return new TreeFolderNode(folder.Name, "folder", children);
    }
}
