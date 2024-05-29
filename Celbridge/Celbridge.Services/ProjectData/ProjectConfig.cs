using SQLite;

namespace Celbridge.BaseLibrary.Project;

public class ProjectConfig : IProjectConfig
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int Version { get; set; }
}

