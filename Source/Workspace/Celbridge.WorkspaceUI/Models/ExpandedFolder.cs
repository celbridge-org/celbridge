using SQLite;

namespace Celbridge.WorkspaceUI.Models;

public class ExpandedFolder
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Folder { get; set; } = string.Empty;
}

