using SQLite;

namespace Celbridge.ProjectAdmin.Models;

public class ProjectDataVersion
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int Version { get; set; }
}
