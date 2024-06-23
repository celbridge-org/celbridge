using Celbridge.BaseLibrary.Project;
using SQLite;

namespace Celbridge.Services.Project;

public class ExpandedFolder
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Folder { get; set; } = string.Empty;
}

