namespace Celbridge.Activities;

/// <summary>
/// A message sent when the activity configuration changes.
/// Activities are configured using components on the project file entity, so this message
/// is broadcast when any changes are made to the project file entity.
/// </summary>
public record ActivityConfigChangedMessage(ResourceKey ProjectFileResource);
