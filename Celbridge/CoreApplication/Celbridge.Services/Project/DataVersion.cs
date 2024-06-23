using Celbridge.BaseLibrary.Project;
using SQLite;

namespace Celbridge.Services.Project;

public class DataVersion
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int Version { get; set; }
}
