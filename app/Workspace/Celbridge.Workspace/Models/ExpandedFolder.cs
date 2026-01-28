using SQLite;

namespace Celbridge.Workspace.Models;

public class ExpandedFolder
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Folder { get; set; } = string.Empty;
}

