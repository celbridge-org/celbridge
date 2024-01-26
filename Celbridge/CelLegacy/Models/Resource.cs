namespace CelLegacy.Models;

public enum ResourceState
{
    Static,
    Added,
    Changed,
}

public record ResourceStatus(Guid ResourceId, ResourceState State);

// Represents a single file or folder in the project folder.
// The name always matches the file/folder name
public abstract class Resource : Entity
{}