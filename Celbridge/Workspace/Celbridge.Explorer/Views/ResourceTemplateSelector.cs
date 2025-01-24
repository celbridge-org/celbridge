using Celbridge.Explorer.Models;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Explorer.Views;

public class ResourceTemplateSelector : DataTemplateSelector
{
    public DataTemplate? FolderTemplate { get; set; }
    public DataTemplate? FileTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
    {
        var treeViewNode = item as TreeViewNode;
        Guard.IsNotNull(treeViewNode);

        if (treeViewNode.Content is FileResource)
        {
            return FileTemplate;
        }
        if (treeViewNode.Content is FolderResource)
        {
            return FolderTemplate;
        }

        throw new NotImplementedException();
    }
}