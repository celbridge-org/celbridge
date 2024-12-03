namespace Celbridge.Entities.Models;

/// <summary>
/// Describes the changes applied by an entity data patch.
/// Includes the original JSON patch, a reverse JSON patch that undoes the changes, and a list of changes.
/// </summary>
public record PatchSummary(string Patch, string ReversePatch, List<ComponentChangedMessage> ComponentChangedMessages);
