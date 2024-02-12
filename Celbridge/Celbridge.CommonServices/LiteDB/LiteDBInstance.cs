using LiteDB;

namespace Celbridge.CommonServices.LiteDB;

public class LiteDBInstance
{
    private readonly ILiteDatabase _database;

    // Constructor to initialize the in-memory database
    public LiteDBInstance()
    {
        // Using ":memory:" to create an in-memory database instance
        _database = new LiteDatabase("Filename=:memory:;Connection=shared");
    }

    // Method to insert a new document into a collection
    public void Insert<T>(string collectionName, T document)
    {
        var collection = _database.GetCollection<T>(collectionName);
        collection.Insert(document);
    }

    // Method to update an existing document in a collection
    public bool Update<T>(string collectionName, T document)
    {
        var collection = _database.GetCollection<T>(collectionName);
        return collection.Update(document);
    }

    // Method to query documents from a collection
    public IEnumerable<T> Query<T>(string collectionName, Query query = null)
    {
        var collection = _database.GetCollection<T>(collectionName);
        return query == null ? collection.FindAll() : collection.Find(query);
    }

    // Method to get a specific document by its ID
    public T FindById<T>(string collectionName, BsonValue id)
    {
        var collection = _database.GetCollection<T>(collectionName);
        return collection.FindById(id);
    }

    // Optionally, include a method to delete a document from a collection
    public bool Delete<T>(string collectionName, BsonValue id)
    {
        var collection = _database.GetCollection<T>(collectionName);
        return collection.Delete(id);
    }

    // Method to ensure indexes on a collection (useful for optimizing queries)
    public void EnsureIndex<T>(string collectionName, string fieldName, bool unique = false)
    {
        var collection = _database.GetCollection<T>(collectionName);
        collection.EnsureIndex(fieldName, unique);
    }

    // Cleanup resources
    public void Dispose()
    {
        _database?.Dispose();
    }
}
