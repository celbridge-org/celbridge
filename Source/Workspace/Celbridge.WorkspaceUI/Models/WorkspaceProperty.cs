using SQLite;

namespace Celbridge.WorkspaceUI.Models;

public class WorkspaceProperty
{
    [PrimaryKey]
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
