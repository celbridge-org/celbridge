# Workspace

The parts of the application outside a document: the panels, the dialogs, the menus, and the project
settings document. Read the [README](README.md) for the invariants, evidence rules and levels.

## Surfaces

The Explorer tree, the Search panel and its field, modal dialogs with text fields, the application's Edit
menu, and the project settings form.

## Cases

| Situation | Action | Expected | Level |
|---|---|---|---|
| A dialog's text field | cut, copy, paste, undo | each acts on the field | 1 |
| The Explorer tree | select all, copy, paste | the verbs act on resources, not on text | 1 |
| A dialog's text field, opened from a focused panel | select all | the field's text is selected; the panel behind keeps its own selection | 2 |
| A dialog with several controls | Tab | focus moves within the dialog | 2 |
| The Search field | paste, select all | each acts on the field, and the Explorer selection is untouched | 2 |
| A field in the project settings document | paste, select all | each acts on the field | 2 |
| A document focused, then the Edit menu opened | inspect | the verbs offered match what that document can actually do | 2 |
| A dialog open | inspect the Edit menu | the verbs are not offered to the surface behind the dialog | 3 |
| A dialog open | shortcuts for close and find | they do not act on the document behind it | 3 |
| Focus on a toolbar or other chrome | copy | the verb still reaches the surface the user was last editing | 3 |

## Not covered

What the commands themselves do — creating, renaming and deleting resources, or the results of a search.
The subject here is which surface a keystroke reaches.
