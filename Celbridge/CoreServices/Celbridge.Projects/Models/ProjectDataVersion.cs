using SQLite;

namespace Celbridge.Projects.Models;

public class ProjectDataVersion
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int Version { get; set; }
}
