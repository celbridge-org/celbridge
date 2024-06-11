using System.Collections.ObjectModel;

namespace Celbridge.Project.Models;

public class FolderResource : Resource
{
    public ObservableCollection<Resource> Children { get; set;  } = new();
}
