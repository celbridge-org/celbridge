using Celbridge.UserInterface;

namespace Celbridge.Resources.Models;

public class FileResource : Resource, IFileResource
{
    public FileIconDefinition Icon { get; }

    public SidecarLink? Sidecar { get; set; }

    // PlainData is the safe default — correct for any non-.cel file. The
    // classifier overwrites it during the project-load walk for the .cel cases
    // before any consumer reads this value.
    public FileKind FileKind { get; set; } = FileKind.PlainData;

    public FileResource(string name, IFolderResource parentFolder, FileIconDefinition icon)
        : base(name, parentFolder)
    {
        Icon = icon;
    }
}
