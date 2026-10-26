using Celbridge.Commands;

namespace Celbridge.Search;

/// <summary>
/// Shows the Search panel with the files to include set to a folder. The command searches when the search field has
/// text.
/// </summary>
public interface ISearchFolderCommand : IExecutableCommand
{
    /// <summary>
    /// The folder to search. The empty key searches the whole project.
    /// </summary>
    ResourceKey Folder { get; set; }
}
