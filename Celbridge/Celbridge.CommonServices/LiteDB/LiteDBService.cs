using Celbridge.BaseLibrary.Core;

namespace Celbridge.CommonServices.LiteDB;

public class LiteDBService
{
    public Result<string> SerializeDatabase(LiteDBInstance database)
    {
        // Todo: Serialize the database to Json format
        // Todo: For CelScripts, this should serialize to multiple Json files
        throw new NotImplementedException();
    }

    public Result<LiteDBInstance> DeserializeDatabase(string databaseJson)
    {
        // Todo: Seerialize the database from Json format
        // Todo: For CelScripts, this should deserialize from multiple Json files and merge the result
        throw new NotImplementedException();
    }
}

