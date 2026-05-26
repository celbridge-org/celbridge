using System.Text;

namespace Celbridge.Resources.Helpers;

/// <summary>
/// Pure tree-walking helpers over an IResource graph. Builds resource keys
/// from an in-tree node (walk parents) and finds a node from a resource key
/// (walk segments). Stateless and independent of the resource registry so
/// the same logic is shared between the registry, the sidecar pairing service,
/// and anyone else who needs to traverse the tree.
/// </summary>
public static class ResourceTreeNavigator
{
    /// <summary>
    /// Walks the parent chain to build the project-relative resource key.
    /// The root folder has a null ParentFolder and contributes no segment.
    /// </summary>
    public static ResourceKey BuildKey(IResource resource)
    {
        try
        {
            var builder = new StringBuilder();
            Append(resource);

            return ResourceKey.Create(builder.ToString());

            void Append(IResource current)
            {
                if (current.ParentFolder is null)
                {
                    return;
                }

                Append(current.ParentFolder);

                if (builder.Length > 0)
                {
                    builder.Append('/');
                }
                builder.Append(current.Name);
            }
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Failed to get resource key for '{resource}'", ex);
        }
    }

    /// <summary>
    /// Walks the segments of the supplied key and returns the matching node
    /// under the root, or a failure result if no match exists. An empty key
    /// resolves to the root itself.
    /// </summary>
    public static Result<IResource> FindResource(IFolderResource root, ResourceKey resource)
    {
        if (resource.IsEmpty)
        {
            return Result<IResource>.Ok(root);
        }

        var segments = resource.Path.Split('/');
        var searchFolder = root;

        var segmentIndex = 0;
        while (segmentIndex < segments.Length)
        {
            IFolderResource? matchingFolder = null;
            string segment = segments[segmentIndex];
            foreach (var childResource in searchFolder.Children)
            {
                if (childResource is IFolderResource childFolder
                    && childFolder.Name == segment)
                {
                    if (segmentIndex == segments.Length - 1)
                    {
                        return Result<IResource>.Ok(childFolder);
                    }

                    matchingFolder = childFolder;
                    break;
                }
                else if (childResource is IFileResource childFile
                         && childFile.Name == segment
                         && segmentIndex == segments.Length - 1)
                {
                    return Result<IResource>.Ok(childFile);
                }
            }

            if (matchingFolder is null)
            {
                break;
            }

            searchFolder = matchingFolder;
            segmentIndex++;
        }

        return Result<IResource>.Fail($"Failed to find a resource matching the resource key '{resource}'.");
    }
}
