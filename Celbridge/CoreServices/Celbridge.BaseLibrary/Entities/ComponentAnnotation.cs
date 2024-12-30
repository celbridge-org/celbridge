namespace Celbridge.Entities;

/// <summary>
/// Contains runtime meta data about the component.
/// This meta data is populated by the activities which operate on this component. To update the annotation, an 
/// activity may use the component's properties, other components in the same entity or any other relevant 
/// information in the project. The annotation data is used to control the behaviour of the component, and its
/// appearance in the editor UI.
/// </summary>
public record ComponentAnnotation(string Description);
