using Celbridge.BaseLibrary.Resources;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Project.Models;

public abstract partial class Resource : ObservableObject, IResource
{
    protected Resource(string name, FolderResource? parentFolder)
    {
        if (parentFolder is not null && 
            string.IsNullOrEmpty(name))
        {
            // The name may be empty for the root node
            throw new ArgumentException($"Argument '{nameof(name)}' must not be empty.");
        }

        Name = name;
        ParentFolder = parentFolder;
    }

    [ObservableProperty]
    private string _name = string.Empty;

    public FolderResource? ParentFolder { get; init; }
}
