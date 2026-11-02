using Celbridge.UserInterface;

namespace Celbridge.Documents;

/// <summary>
/// A state of a custom editor, shown to the user as a pip on the editor's tab or rail button. The label is one
/// line, already localized by the editor.
/// </summary>
public sealed record DocumentStatus(StatusKind Kind, string Label);
