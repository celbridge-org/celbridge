using SQLite;

namespace Celbridge.Workspace.Services;

public class ExpandedFolder
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Folder { get; set; } = string.Empty;
}

