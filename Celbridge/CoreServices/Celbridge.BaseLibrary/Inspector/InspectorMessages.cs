namespace Celbridge.Inspector;

/// <summary>
/// A message sent when the user has selected a component in the component list.
/// </summary>
public record SelectedComponentChangedMessage(int componentIndex);

/// <summary>
/// A message sent when the inspected component has changed.
/// </summary>
public record InspectedComponentChangedMessage(ResourceKey Resource, int ComponentIndex);

/// <summary>
/// A message sent when the component panel edit mode changes.
/// </summary>
public record ComponentPanelModeChangedMessage(ComponentPanelMode EditMode);

/// <summary>
/// A message sent when the component type input text changes.
/// This message is sent as the user types text into the component type input box.
/// </summary>
public record ComponentTypeTextChangedMessage(string ComponentType);

/// <summary>
/// A message sent when the user presses the enter key in the component type input box.
/// </summary>
public record ComponentTypeTextEnteredMessage();

/// <summary>
/// A message sent when the component list in the inspector has been populated.
/// </summary>
public record PopulatedComponentListMessage(ResourceKey Resource);
