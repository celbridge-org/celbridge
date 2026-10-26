using Celbridge.Commands;

namespace Celbridge.Search;

/// <summary>
/// Shows the Search panel on a folder, and searches the folder when the search field has text. The command leaves
/// the search text and the search options unchanged.
/// </summary>
public interface ISearchFolderCommand : IExecutableCommand
{
    /// <summary>
    /// The folder to search. The empty key searches the whole project.
    /// </summary>
    ResourceKey Folder { get; set; }
}
