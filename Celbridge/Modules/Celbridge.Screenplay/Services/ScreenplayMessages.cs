namespace Celbridge.Screenplay.Services;

/// <summary>
/// Message sent when an error is found while attempting to save a screenplay.
/// </summary>
public record SaveScreenplayErrorMessage(ResourceKey SceneResource);
