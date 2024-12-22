namespace Celbridge.Inspector;

/// <summary>
/// Contains runtime meta data about the component.
/// This data is populated by the Activity which manages the component, using the properties of the 
/// component itself, other components in the same and any other relevant information in the project.
/// This data is used to control the behaviour and appearance of the component to the user.
/// </summary>
public record ComponentAnnotation(string Description);
