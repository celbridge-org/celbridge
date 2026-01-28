using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Explorer.Models;

public abstract partial class Resource : ObservableObject, IResource
{
    protected Resource(string name, IFolderResource? parentFolder)
    {
        if (parentFolder is not null && 
            string.IsNullOrEmpty(name))
        {
            // The name is allowed to be empty for the root node (parentFolder is null)
            throw new ArgumentException($"Argument '{nameof(name)}' must not be empty.");
        }

        Name = name;
        ParentFolder = parentFolder;
    }

    [ObservableProperty]
    private string _name = string.Empty;

    public Guid ResourceId { get; } = Guid.NewGuid();

    public IFolderResource? ParentFolder { get; init; }

}
