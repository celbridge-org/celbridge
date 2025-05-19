namespace Celbridge.Screenplay.Services;

/// <summary>
/// Message sent when the SaveScreenplay command has failed because of an error in a scene resource.
/// </summary>
public record SaveScreenplayFailedMessage(ResourceKey SceneResource);

/// <summary>
/// Message sent when the SaveScreenplay command has succeeded.
/// </summary>
public record SaveScreenplaySucceededMessage();
