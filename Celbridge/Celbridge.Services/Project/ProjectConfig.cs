using Celbridge.BaseLibrary.Project;
using SQLite;

namespace Celbridge.Services.Project;

public class ProjectConfig : IProjectConfig
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int Version { get; set; }
}

