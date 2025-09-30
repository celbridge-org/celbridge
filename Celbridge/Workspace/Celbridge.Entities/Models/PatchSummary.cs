using Json.Patch;

namespace Celbridge.Entities.Models;

/// <summary>
/// Describes the changes applied by an entity patch operation.
/// Includes the original patch operation, a reverse operation that undoes the changes, and a message object describing the change.
/// </summary>
public record PatchSummary(PatchOperation Operation, PatchOperation? ReverseOperation, ComponentChangedMessage? ComponentChangedMessage, long UndoGroupId);
