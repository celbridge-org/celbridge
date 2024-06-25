using SQLite;

namespace Celbridge.Services.Project;

public class ProjectDataVersion
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int Version { get; set; }
}
