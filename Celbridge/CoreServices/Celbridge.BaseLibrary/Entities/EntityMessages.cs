namespace Celbridge.Entities;

/// <summary>
/// A message describing a change made to one or more entity properties.
/// Includes a JSON Patch that can be used to reverse the changes.
/// </summary>
public record EntityChangedMessage(ResourceKey Resource, List<string> PropertyPaths);
